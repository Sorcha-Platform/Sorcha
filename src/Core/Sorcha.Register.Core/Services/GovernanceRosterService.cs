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

        // The latest Control TX contains the full current roster
        var latestControlTx = controlTransactions[^1];
        var payload = DeserializeControlPayload(latestControlTx);

        if (payload?.Roster == null)
        {
            _logger.LogWarning("Latest Control transaction for register {RegisterId} has no roster payload", registerId);
            return null;
        }

        _logger.LogInformation(
            "Reconstructed roster for register {RegisterId}: {MemberCount} members from {TxCount} Control transactions",
            registerId, payload.Roster.Attestations.Count, controlTransactions.Count);

        return new AdminRoster
        {
            RegisterId = registerId,
            ControlRecord = payload.Roster,
            ControlTransactionCount = controlTransactions.Count,
            LastControlTxId = latestControlTx.TxId
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

                // Demote old Owner to Admin
                updatedAttestations.Remove(oldOwner);
                updatedAttestations.Add(new RegisterAttestation
                {
                    Role = RegisterRole.Admin,
                    Subject = oldOwner.Subject,
                    PublicKey = oldOwner.PublicKey,
                    Signature = oldOwner.Signature,
                    Algorithm = oldOwner.Algorithm,
                    GrantedAt = DateTimeOffset.UtcNow
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
                    GrantedAt = DateTimeOffset.UtcNow
                });

                _logger.LogInformation("Transferred ownership from {OldOwner} to {NewOwner}",
                    oldOwner.Subject, targetAttestation.Subject);
                break;
        }

        return new RegisterControlRecord
        {
            RegisterId = currentRoster.RegisterId,
            Name = currentRoster.Name,
            Description = currentRoster.Description,
            TenantId = currentRoster.TenantId,
            CreatedAt = currentRoster.CreatedAt,
            Attestations = updatedAttestations,
            Metadata = currentRoster.Metadata
        };
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
        // Query repository directly instead of HTTP calls
        var allTransactions = await _repository.GetTransactionsAsync(registerId, cancellationToken);

        var controlTxs = allTransactions
            .Where(t => t.MetaData != null && t.MetaData.TransactionType == TransactionType.Control)
            .OrderBy(t => t.DocketNumber ?? 0)  // Order by docket for correct roster reconstruction
            .ToList();

        return controlTxs;
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
