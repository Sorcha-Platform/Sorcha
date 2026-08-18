// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Core.Services;

/// <summary>
/// Reconstructs and manages register governance rosters from Control transactions
/// </summary>
public class GovernanceRosterService : IGovernanceRosterService
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly ILogger<GovernanceRosterService> _logger;

    public GovernanceRosterService(
        IReadOnlyRegisterRepository repository,
        ILogger<GovernanceRosterService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<AdminRoster?> GetCurrentRosterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        _logger.LogDebug("Reconstructing governance roster for register {RegisterId}", registerId);

        var controlTransactions = await GetControlTransactionsAsync(registerId, cancellationToken);

        if (controlTransactions.Count == 0)
        {
            _logger.LogWarning("No Control transactions found for register {RegisterId}", registerId);
            return null;
        }

        // Walk newest → oldest and pick the first transaction whose payload actually deserialises
        // to a populated ControlTransactionPayload (i.e. carries a real roster snapshot).
        //
        // TransactionType.Control is broader than "governance roster snapshot" — action
        // submissions and other non-roster paths can also land with type=Control depending on the
        // writer. Picking the LAST control tx unconditionally is therefore fragile: a single
        // non-roster control tx (e.g. an action 1 submission with type=0 left in the register
        // during cross-day n1 runs) silently wipes the F142 PublishGate's view of the roster
        // (the policy endpoint returns members:[] and every publish refuses with
        // "lacks publish-governance role").
        //
        // This loop is defensive — for a normal register with N genuine governance commits, it
        // immediately picks the newest one; for a register that has accumulated a non-roster
        // Control tx newer than the latest real roster commit, it skips past the noise.
        TransactionModel? matchedTx = null;
        ControlTransactionPayload? payload = null;
        for (var i = controlTransactions.Count - 1; i >= 0; i--)
        {
            var candidate = controlTransactions[i];
            var candidatePayload = DeserializeControlPayload(candidate);
            if (candidatePayload?.Roster == null) continue;

            // A populated roster snapshot has at least one attestation OR a non-null Validators
            // section. A wrapper that deserialised silently from non-governance bytes will have
            // Roster set (default-constructed) but no attestations and no validators — treat
            // that as "this control tx does not carry a real roster" and keep scanning.
            var roster = candidatePayload.Roster;
            var hasAttestations = roster.Attestations is { Count: > 0 };
            var hasValidatorRoster = roster.Validators is not null
                && roster.Validators.Validators is { Count: > 0 };
            if (!hasAttestations && !hasValidatorRoster) continue;

            matchedTx = candidate;
            payload = candidatePayload;
            break;
        }

        if (matchedTx is null || payload?.Roster is null)
        {
            _logger.LogWarning(
                "No Control transaction for register {RegisterId} carries a populated roster payload ({TxCount} candidates scanned)",
                registerId, controlTransactions.Count);
            return null;
        }

        _logger.LogInformation(
            "Reconstructed roster for register {RegisterId}: {MemberCount} members from {TxCount} Control transactions (latest-with-roster: {TxId})",
            registerId, payload.Roster.Attestations.Count, controlTransactions.Count, matchedTx.TxId);

        return new AdminRoster
        {
            RegisterId = registerId,
            ControlRecord = payload.Roster,
            ControlTransactionCount = controlTransactions.Count,
            LastControlTxId = matchedTx.TxId
        };
    }

    /// <inheritdoc/>
    public async Task<QuorumResult> ValidateQuorumAsync(
        string registerId,
        GovernanceOperation operation,
        List<ApprovalSignature> approvals,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(approvals);

        var roster = await GetCurrentRosterAsync(registerId, cancellationToken);
        if (roster == null)
            return new QuorumResult { IsQuorumMet = false, VotesRequired = 1, VotesReceived = 0, VotingPool = 0 };

        var controlRecord = roster.ControlRecord;

        // Check if proposer is Owner (Owner override bypasses quorum)
        var ownerDid = controlRecord.GetSubjectsWithRole(RegisterRole.Owner).FirstOrDefault();
        if (ownerDid != null && ownerDid == operation.ProposerDid &&
            operation.OperationType != GovernanceOperationType.Transfer)
        {
            _logger.LogInformation(
                "Owner override for {OperationType} on register {RegisterId}",
                operation.OperationType, registerId);

            return new QuorumResult
            {
                IsQuorumMet = true,
                VotesRequired = 1,
                VotesReceived = 1,
                VotingPool = 1,
                IsOwnerOverride = true
            };
        }

        // For Remove operations, exclude the target from the voting pool
        string? excludeDid = operation.OperationType == GovernanceOperationType.Remove
            ? operation.TargetDid
            : null;

        // Read quorum formula from register policy (default to StrictMajority for backward compatibility)
        var formula = controlRecord.RegisterPolicy?.Governance?.QuorumFormula ?? QuorumFormula.StrictMajority;
        var threshold = controlRecord.GetQuorumThreshold(excludeDid, formula);
        var votingMembers = controlRecord.GetVotingMembers();

        if (excludeDid != null)
        {
            votingMembers = votingMembers.Where(a => a.Subject != excludeDid);
        }

        var votingPool = votingMembers.Count();

        // Count valid approval votes (from roster members only)
        var validApprovals = approvals
            .Where(a => a.IsApproval)
            .Where(a => votingMembers.Any(m => m.Subject == a.ApproverDid))
            .ToList();

        var isQuorumMet = validApprovals.Count >= threshold;

        _logger.LogInformation(
            "Quorum check for {OperationType} on register {RegisterId}: {Votes}/{Required} (pool={Pool}, met={Met})",
            operation.OperationType, registerId, validApprovals.Count, threshold, votingPool, isQuorumMet);

        return new QuorumResult
        {
            IsQuorumMet = isQuorumMet,
            VotesRequired = threshold,
            VotesReceived = validApprovals.Count,
            VotingPool = votingPool
        };
    }

    /// <inheritdoc/>
    public GovernanceValidationResult ValidateProposal(
        AdminRoster roster,
        GovernanceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(operation);

        var errors = new List<string>();
        var controlRecord = roster.ControlRecord;

        // Validate proposer is in the roster with a voting role
        var proposerAttestation = controlRecord.Attestations
            .FirstOrDefault(a => a.Subject == operation.ProposerDid);

        if (proposerAttestation == null)
        {
            errors.Add($"Proposer '{operation.ProposerDid}' is not in the roster");
            return GovernanceValidationResult.Failure(errors.ToArray());
        }

        if (proposerAttestation.Role is not (RegisterRole.Owner or RegisterRole.Admin))
        {
            errors.Add($"Proposer '{operation.ProposerDid}' has role '{proposerAttestation.Role}' which cannot propose governance operations");
        }

        // Check proposal expiry
        if (operation.ExpiresAt != default && operation.ExpiresAt < DateTimeOffset.UtcNow)
        {
            errors.Add("Proposal has expired");
        }

        switch (operation.OperationType)
        {
            case GovernanceOperationType.Add:
                ValidateAddProposal(controlRecord, operation, errors);
                break;
            case GovernanceOperationType.Remove:
                ValidateRemoveProposal(controlRecord, operation, errors);
                break;
            case GovernanceOperationType.Transfer:
                ValidateTransferProposal(controlRecord, operation, proposerAttestation, errors);
                break;
        }

        return errors.Count > 0
            ? GovernanceValidationResult.Failure(errors.ToArray())
            : GovernanceValidationResult.Success();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>Deterministic by construction (Feature 189).</b> Every timestamp written here derives from
    /// <c>operation.ProposedAt</c> — sealed content, identical on every node — and never from the
    /// clock. The Transfer arms used <c>DateTimeOffset.UtcNow</c>, which meant two nodes enacting the
    /// same proposal produced <i>different bytes</i>, and therefore the same deterministic
    /// transaction id with a different payload hash: a conflicting duplicate that reads as a fork
    /// rather than as the idempotent resubmission it is. There is no symptom until two nodes enact
    /// concurrently, so this cannot be caught by inspection —
    /// <c>ApplyOperationDeterminismTests</c> is the guard.
    /// </remarks>
    public RegisterControlRecord ApplyOperation(
        RegisterControlRecord currentRoster,
        GovernanceOperation operation,
        RegisterAttestation? newAttestation = null)
    {
        ArgumentNullException.ThrowIfNull(currentRoster);
        ArgumentNullException.ThrowIfNull(operation);

        // Clone the attestations list
        var updatedAttestations = currentRoster.Attestations.ToList();

        switch (operation.OperationType)
        {
            case GovernanceOperationType.Add:
                if (newAttestation == null)
                    throw new ArgumentException("New attestation required for Add operation", nameof(newAttestation));
                updatedAttestations.Add(newAttestation);
                _logger.LogInformation("Added member {TargetDid} with role {Role} to roster",
                    operation.TargetDid, operation.TargetRole);
                break;

            case GovernanceOperationType.Remove:
                updatedAttestations.RemoveAll(a => a.Subject == operation.TargetDid);
                _logger.LogInformation("Removed member {TargetDid} from roster", operation.TargetDid);
                break;

            case GovernanceOperationType.Transfer:
                // Promote target to Owner
                var targetAttestation = updatedAttestations.First(a => a.Subject == operation.TargetDid);
                var oldOwner = updatedAttestations.First(a => a.Role == RegisterRole.Owner);

                // #1464 — an Owner with no public key cannot govern the register it owns. Roster
                // authority is matched BY KEY (GovernanceKeyMatcher returns false for an empty one,
                // deliberately), so such an Owner can never sign a governance transaction, its
                // approvals are excluded from every tally, and SelectSigner prefers the Owner — so
                // there is no way to route around it. The register is permanently ungovernable.
                //
                // Demonstrated live on n1: the promoted Owner's next proposal returned HTTP 200 and
                // never sealed (VAL_PERM_002). The HTTP layer reported success throughout.
                //
                // Refuse here rather than upstream because every enactment path — both Add sites and
                // the propose-and-enact override — funnels through this method, so this is the one
                // place the bad end-state can be made unreachable rather than merely unlikely.
                //
                // Note this guards who GAINS authority, not who loses it: transferring away from an
                // already-unkeyed Owner is the repair for a register in that state, and refusing it
                // would make the damage permanent.
                if (string.IsNullOrWhiteSpace(targetAttestation.PublicKey))
                {
                    throw new InvalidOperationException(
                        $"Cannot transfer ownership to '{operation.TargetDid}': the roster holds no "
                        + "public key for it, so it could never sign a governance transaction and the "
                        + "register would become permanently ungovernable (#1464).");
                }

                // Demote old Owner to Admin
                updatedAttestations.Remove(oldOwner);
                updatedAttestations.Add(new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = oldOwner.Subject,
                    PublicKey = oldOwner.PublicKey,
                    Signature = oldOwner.Signature,
                    Algorithm = oldOwner.Algorithm,
                    GrantedAt = operation.ProposedAt
                });

                // Promote target to Owner
                updatedAttestations.Remove(targetAttestation);
                updatedAttestations.Add(new RegisterAttestation
                {
                    Role = RegisterRole.Owner,
                    Subject = targetAttestation.Subject,
                    PublicKey = targetAttestation.PublicKey,
                    Signature = targetAttestation.Signature,
                    Algorithm = targetAttestation.Algorithm,
                    GrantedAt = operation.ProposedAt
                });

                _logger.LogInformation("Transferred ownership from {OldOwner} to {NewOwner}",
                    oldOwner.Subject, targetAttestation.Subject);
                break;
        }

        // A governance operation changes MEMBERSHIP. Everything else about the register — its crypto
        // policy, its governance policy, its routing-attestation strength, its validator roster —
        // must survive being governed.
        //
        // This was an object initializer naming six of the ten properties, so the other four were
        // dropped by every enacted Add/Remove/Transfer. Nothing failed: the validator roster is
        // resolved from the GENESIS docket by both of its readers, so dockets kept sealing. What
        // moved was the governance rule itself — ValidateQuorumAsync reads the quorum formula from
        // RegisterPolicy.Governance, so a consortium register set to Unanimous fell back to the
        // StrictMajority default the moment its first change enacted, turning three-of-three into
        // two-of-three with nothing reported. It also silently discarded the work of
        // GovernanceEnactmentService.ApplyValidatorRosterChange, which updates Validators and then
        // hands the record straight to this method.
        //
        // Cloned rather than re-listed so the next property added to RegisterControlRecord is
        // carried forward on the day it is added. Guarded by
        // ApplyOperationPreservesRegisterConfigurationTests, which asserts by reflection.
        var updated = currentRoster.ShallowCopy();
        updated.Attestations = updatedAttestations;
        return updated;
    }

    private static void ValidateAddProposal(
        RegisterControlRecord controlRecord, GovernanceOperation operation, List<string> errors)
    {
        // Target must not already be in roster
        if (controlRecord.Attestations.Any(a => a.Subject == operation.TargetDid))
        {
            errors.Add($"Target '{operation.TargetDid}' is already in the roster");
        }

        // Roster cap check
        if (controlRecord.Attestations.Count >= 25)
        {
            errors.Add("Roster has reached maximum capacity (25 members)");
        }
    }

    private static void ValidateRemoveProposal(
        RegisterControlRecord controlRecord, GovernanceOperation operation, List<string> errors)
    {
        // Target must exist in roster
        var target = controlRecord.Attestations.FirstOrDefault(a => a.Subject == operation.TargetDid);
        if (target == null)
        {
            errors.Add($"Target '{operation.TargetDid}' is not in the roster");
        }
        else if (target.Role == RegisterRole.Owner)
        {
            errors.Add("Cannot remove Owner via Remove operation — use Transfer instead");
        }
    }

    private static void ValidateTransferProposal(
        RegisterControlRecord controlRecord, GovernanceOperation operation,
        RegisterAttestation proposerAttestation, List<string> errors)
    {
        // Only Owner can propose transfer
        if (proposerAttestation.Role != RegisterRole.Owner)
        {
            errors.Add("Only the Owner can propose an ownership transfer");
        }

        // Target must be an existing Admin
        var target = controlRecord.Attestations.FirstOrDefault(a => a.Subject == operation.TargetDid);
        if (target == null)
        {
            errors.Add($"Transfer target '{operation.TargetDid}' is not in the roster");
        }
        else if (target.Role != RegisterRole.Admin)
        {
            errors.Add($"Transfer target must be an existing Admin, but has role '{target.Role}'");
        }
    }

    private async Task<List<TransactionModel>> GetControlTransactionsAsync(
        string registerId, CancellationToken cancellationToken)
    {
        // Pushed down to the store: filter to Control + sort by docket ascending (index-backed),
        // rather than materialising the whole ledger and filtering/sorting in memory.
        var controlTxs = await _repository.GetTransactionsByTypeAsync(
            registerId,
            TransactionType.Control,
            TransactionSort.DocketNumberAscending, // apply in docket order for correct roster reconstruction
            cancellationToken: cancellationToken);

        return controlTxs.ToList();
    }

    private ControlTransactionPayload? DeserializeControlPayload(TransactionModel transaction)
    {
        try
        {
            if (transaction.Payloads == null || transaction.Payloads.Length == 0)
                return null;

            var payloadData = transaction.Payloads[0].Data;
            if (string.IsNullOrWhiteSpace(payloadData))
                return null;

            // Smart decode: legacy Base64 (+, /, =) or Base64url
            var payloadBytes = payloadData.Contains('+') || payloadData.Contains('/') || payloadData.Contains('=')
                ? Convert.FromBase64String(payloadData)
                : System.Buffers.Text.Base64Url.DecodeFromChars(payloadData);
            return JsonSerializer.Deserialize<ControlTransactionPayload>(payloadBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Control transaction payload for TX {TxId}", transaction.TxId);
            return null;
        }
    }
}
