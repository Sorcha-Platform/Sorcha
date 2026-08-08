// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Register.Service.Services;

/// <summary>What happened when enactment was attempted.</summary>
public enum EnactmentOutcome
{
    /// <summary>An enactment transaction was submitted. It takes effect when it seals.</summary>
    Submitted = 0,

    /// <summary>Not enough approvals have sealed yet. The ordinary state of an open proposal.</summary>
    QuorumNotMet,

    /// <summary>An enactment for this proposal already exists.</summary>
    AlreadyEnacted,

    /// <summary>The proposal cannot be enacted — missing, decided, expired, or invalidated.</summary>
    NotEnactable,

    /// <summary>This node holds no governance key for the register, so it cannot carry the enactment.</summary>
    CannotCarry,

    /// <summary>The Validator refused it.</summary>
    Refused,
}

/// <summary>Outcome of an enactment attempt.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="TxId">Enactment transaction id, when one was built.</param>
/// <param name="Detail">Operator-facing explanation. Always populated.</param>
public sealed record GovernanceEnactmentResult(EnactmentOutcome Outcome, string? TxId, string Detail);

/// <summary>
/// Raises the enactment transaction for a proposal whose approvals have reached quorum.
/// </summary>
public interface IGovernanceEnactmentService
{
    /// <summary>
    /// Enacts the proposal if it is due, and does nothing otherwise. Safe to call repeatedly.
    /// </summary>
    Task<GovernanceEnactmentResult> TryEnactAsync(
        string registerId, string proposalId, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// <b>This decides whether to TRY, not whether the change is authorised.</b> The Validator is the
/// authority: it re-counts from the sealed approvals, verifies every signature, and refuses an
/// enactment that has not reached quorum. So this deliberately uses only the <i>structural</i> half of
/// <see cref="GovernanceApprovalTally"/> — who is on the roster, with the roster's key, not counted
/// twice — and never verifies a signature itself.
/// </para>
/// <para>
/// That is a correctness decision, not a shortcut. Verifying here as well would put the same crypto
/// loop in two services, and two implementations of one rule is how they come to disagree about
/// whether a register's governance change is authorised. The worst case of trusting the Validator is a
/// doomed enactment that gets refused and logged; the worst case of a second opinion is a node that
/// enacts something the network will not accept, or refuses something it would.
/// </para>
/// <para>
/// <b>Every value in the payload derives from sealed content.</b> Two nodes may reach quorum in the
/// same moment and both submit; the transaction id is deterministic so the second dedupes rather than
/// forking, and that only holds if the bytes match. No clock is read while building the payload.
/// </para>
/// </remarks>
public sealed class GovernanceEnactmentService : IGovernanceEnactmentService
{
    private readonly IGovernanceProposalReader _proposalReader;
    private readonly IGovernanceRosterService _rosterService;
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly IGovernanceSigningService _signingService;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ILogger<GovernanceEnactmentService> _logger;

    /// <summary>Initialises a new instance of the <see cref="GovernanceEnactmentService"/> class.</summary>
    public GovernanceEnactmentService(
        IGovernanceProposalReader proposalReader,
        IGovernanceRosterService rosterService,
        IReadOnlyRegisterRepository repository,
        IGovernanceSigningService signingService,
        IValidatorServiceClient validatorClient,
        ILogger<GovernanceEnactmentService> logger)
    {
        _proposalReader = proposalReader ?? throw new ArgumentNullException(nameof(proposalReader));
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GovernanceEnactmentResult> TryEnactAsync(
        string registerId, string proposalId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        var read = await _proposalReader.ReadAsync(registerId, proposalId, ct);
        if (read.Outcome != ProposalReadOutcome.Found)
        {
            return Result(EnactmentOutcome.NotEnactable, null,
                $"Proposal '{proposalId}' could not be read ({read.Outcome}).");
        }

        var operation = read.Operation!;

        if (operation.Status != ProposalStatus.Pending)
        {
            return Result(EnactmentOutcome.NotEnactable, null,
                $"Proposal '{proposalId}' is {operation.Status}, not open.");
        }

        var roster = await _rosterService.GetCurrentRosterAsync(registerId, ct);
        if (roster is null)
        {
            return Result(EnactmentOutcome.NotEnactable, null,
                $"Register '{registerId}' has no governance roster.");
        }

        // FR-011b. A proposal raised against a roster that has since moved on is dead, and enacting it
        // would apply a change judged against a pool that no longer exists.
        if (!string.IsNullOrEmpty(operation.RosterSnapshotId) &&
            !string.Equals(operation.RosterSnapshotId, roster.LastControlTxId, StringComparison.Ordinal))
        {
            return Result(EnactmentOutcome.NotEnactable, null,
                $"Proposal '{proposalId}' was raised against roster '{operation.RosterSnapshotId}' but the "
                + $"register is now on '{roster.LastControlTxId}'.");
        }

        var enactmentTxId = ComputeEnactmentTransactionId(registerId, proposalId);

        // The deterministic id doubles as the existence check: if it is already on the register, this
        // proposal has been enacted, whichever node got there first.
        if (await _repository.GetTransactionAsync(registerId, enactmentTxId, ct) is not null)
        {
            return Result(EnactmentOutcome.AlreadyEnacted, enactmentTxId,
                $"Proposal '{proposalId}' was already enacted as '{enactmentTxId}'.");
        }

        var votes = await CollectStructurallyEligibleVotesAsync(registerId, proposalId, roster, operation, ct);

        var quorum = await _rosterService.ValidateQuorumAsync(registerId, operation, votes, ct);
        if (!quorum.IsQuorumMet)
        {
            return Result(EnactmentOutcome.QuorumNotMet, null,
                $"Proposal '{proposalId}': {quorum.VotesReceived}/{quorum.VotesRequired} approvals "
                + $"(pool {quorum.VotingPool}).");
        }

        return await SubmitEnactmentAsync(registerId, proposalId, enactmentTxId, roster, operation, ct);
    }

    /// <summary>
    /// The approvals that could count, by structure alone. Signature verification is the Validator's.
    /// </summary>
    private async Task<List<ApprovalSignature>> CollectStructurallyEligibleVotesAsync(
        string registerId,
        string proposalId,
        AdminRoster roster,
        GovernanceOperation operation,
        CancellationToken ct)
    {
        // Approvals chain off the proposal they answer.
        var successors = await _repository.GetTransactionsByPrevTxIdAsync(registerId, proposalId, ct);

        var approvals = new List<GovernanceApprovalActionPayload>();
        foreach (var candidate in successors.OrderBy(t => t.DocketNumber ?? 0))
        {
            var approval = TryDecodeApproval(candidate);
            if (approval is not null && string.Equals(
                    approval.Type, GovernanceApprovalActionPayload.PayloadType, StringComparison.Ordinal))
            {
                approvals.Add(approval);
            }
        }

        var plan = GovernanceApprovalTally.Prepare(
            registerId, operation, roster.ControlRecord, approvals);

        foreach (var excluded in plan.Excluded)
        {
            _logger.LogInformation(
                "Approval from {ApproverDid} on proposal {ProposalId} cannot count: {Reason}",
                excluded.ApproverDid, proposalId, excluded.Refusal);
        }

        // Every structurally eligible check is treated as a vote here — that is the optimism this
        // class is allowed. A forged signature among them makes the enactment fail at the Validator,
        // which is the correct place for it to fail.
        return GovernanceApprovalTally.ToVotes(
            plan, plan.Checks.Select(c => c.ApproverDid).ToHashSet(StringComparer.Ordinal));
    }

    private async Task<GovernanceEnactmentResult> SubmitEnactmentAsync(
        string registerId,
        string proposalId,
        string enactmentTxId,
        AdminRoster roster,
        GovernanceOperation operation,
        CancellationToken ct)
    {
        ControlTransactionPayload payload;
        try
        {
            payload = BuildEnactmentPayload(roster, operation, proposalId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result(EnactmentOutcome.NotEnactable, null,
                $"Proposal '{proposalId}' cannot be applied to the current roster: {ex.Message}");
        }

        var canonicalJson = JsonSerializer.Serialize(payload, CanonicalJsonOptions);
        var payloadElement = JsonDocument.Parse(canonicalJson).RootElement;
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

        GovernanceSignResult signed;
        try
        {
            signed = await _signingService.SignAsync(registerId, enactmentTxId, payloadHash, null, ct);
        }
        catch (InvalidOperationException ex)
        {
            // This node governs nothing on this register, so it is not the one to carry the
            // enactment. Not an error: another node that does will.
            _logger.LogDebug(ex,
                "Not carrying the enactment of proposal {ProposalId} on register {RegisterId} — no "
                + "governance key here", proposalId, registerId);
            return Result(EnactmentOutcome.CannotCarry, enactmentTxId,
                "This node holds no governance key for the register.");
        }

        var submission = new TransactionSubmission
        {
            TransactionId = enactmentTxId,
            RegisterId = registerId,
            BlueprintId = GovernanceBlueprint.BlueprintId,
            ActionId = GovernanceBlueprint.RecordControlTransactionActionId.ToString(),
            Payload = payloadElement,
            PayloadHash = payloadHash,
            // The enactment IS the roster mutation, so it chains off the roster head.
            PreviousTransactionId = roster.LastControlTxId,
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Base64Url.EncodeToString(signed.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(signed.Signature),
                    Algorithm = signed.Algorithm,
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["Type"] = "Control",
                ["transactionType"] = "GovernanceOperation",
                ["operationType"] = operation.OperationType.ToString().ToLowerInvariant(),
                ["proposerDid"] = operation.ProposerDid,
                ["targetDid"] = operation.TargetDid,
                ["enactsProposalId"] = proposalId,
                ["proposalStatus"] = ProposalStatus.Recorded.ToString(),
                ["carriedBy"] = signed.Subject,
                ["SystemWalletAddress"] = signed.WalletAddress,
            },
        };

        var result = await _validatorClient.SubmitTransactionAsync(submission, ct);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Enactment {TxId} of proposal {ProposalId} on register {RegisterId} refused: {Error}",
                enactmentTxId, proposalId, registerId, result.ErrorMessage);
            return Result(EnactmentOutcome.Refused, enactmentTxId,
                result.ErrorMessage ?? "The Validator refused the enactment.");
        }

        _logger.LogInformation(
            "Proposal {ProposalId} on register {RegisterId} reached quorum; enactment {TxId} submitted, "
            + "carried by {CarriedBy} — it takes effect when it seals",
            proposalId, registerId, enactmentTxId, signed.Subject);

        return Result(EnactmentOutcome.Submitted, enactmentTxId, "Enactment submitted.");
    }

    /// <summary>
    /// Builds the enacting payload with every value derived from sealed content.
    /// </summary>
    /// <remarks>
    /// <b>No clock is read here.</b> Two nodes may enact the same proposal at the same moment; the
    /// deterministic transaction id makes the second a resubmission rather than a fork, and that holds
    /// only while the bytes match. <c>operation.ProposedAt</c> is the source for every timestamp —
    /// it is inside the proposal, inside the approval digest, and identical everywhere.
    /// </remarks>
    internal ControlTransactionPayload BuildEnactmentPayload(
        AdminRoster roster, GovernanceOperation proposed, string proposalId)
    {
        // The operation is carried forward as proposed, with only its lifecycle status advanced. The
        // Validator rebuilds each approval digest from the PROPOSAL's stored operation, so nothing
        // here can change what those signatures are taken to have authorised.
        var enacted = CloneWithStatus(proposed, ProposalStatus.Recorded);

        RegisterAttestation? newAttestation = null;
        if (enacted.OperationType == GovernanceOperationType.Add)
        {
            newAttestation = new RegisterAttestation
            {
                Role = enacted.TargetRole,
                Subject = enacted.TargetDid,
                PublicKey = string.Empty,
                Signature = string.Empty,
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = enacted.ProposedAt,
            };
        }

        var control = ApplyValidatorRosterChange(roster.ControlRecord, enacted);
        var updated = _rosterService.ApplyOperation(control, enacted, newAttestation);

        return new ControlTransactionPayload
        {
            Version = 1,
            Roster = updated,
            Operation = enacted,
            EnactsProposalId = proposalId,
        };
    }

    /// <summary>
    /// Applies a validator-roster operation, with the same sealed-content timestamps.
    /// </summary>
    private static RegisterControlRecord ApplyValidatorRosterChange(
        RegisterControlRecord control, GovernanceOperation operation)
    {
        if (operation.OperationType is not (GovernanceOperationType.AddValidator
            or GovernanceOperationType.RemoveValidator
            or GovernanceOperationType.RotateValidatorKey))
        {
            return control;
        }

        var validators = control.Validators ?? new ValidatorRoster { Version = 0 };

        switch (operation.OperationType)
        {
            case GovernanceOperationType.AddValidator:
                var added = operation.ValidatorEntry
                    ?? throw new InvalidOperationException("ValidatorEntry is required for AddValidator");
                added.AuthorizedAt = operation.ProposedAt;
                added.Status = ValidatorKeyStatus.Active;
                validators.Validators.Add(added);
                break;

            case GovernanceOperationType.RemoveValidator:
                var revoked = validators.Validators.FirstOrDefault(v => v.ValidatorId == operation.TargetDid)
                    ?? throw new InvalidOperationException(
                        $"Validator '{operation.TargetDid}' is not on the roster");
                if (validators.ActiveValidators.Count() <= 1)
                {
                    throw new InvalidOperationException("Cannot remove the last active validator");
                }

                revoked.Status = ValidatorKeyStatus.Revoked;
                revoked.RevokedAt = operation.ProposedAt;
                break;

            case GovernanceOperationType.RotateValidatorKey:
                var replacement = operation.ValidatorEntry
                    ?? throw new InvalidOperationException("ValidatorEntry is required for RotateValidatorKey");
                var rotating = validators.Validators.FirstOrDefault(
                        v => v.ValidatorId == operation.TargetDid && v.Status == ValidatorKeyStatus.Active)
                    ?? throw new InvalidOperationException(
                        $"Active validator '{operation.TargetDid}' is not on the roster");

                rotating.Status = ValidatorKeyStatus.Rotated;
                rotating.RevokedAt = operation.ProposedAt;
                replacement.AuthorizedAt = operation.ProposedAt;
                replacement.Status = ValidatorKeyStatus.Active;
                validators.Validators.Add(replacement);
                break;
        }

        validators.Version++;

        var errors = validators.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Validator roster validation failed: " + string.Join("; ", errors));
        }

        control.Validators = validators;
        return control;
    }

    private static GovernanceOperation CloneWithStatus(GovernanceOperation source, ProposalStatus status)
    {
        var clone = JsonSerializer.Deserialize<GovernanceOperation>(
            JsonSerializer.Serialize(source, CanonicalJsonOptions), CanonicalJsonOptions)!;
        clone.Status = status;
        return clone;
    }

    /// <summary>One enactment per (register, proposal), so concurrent nodes dedupe rather than fork.</summary>
    internal static string ComputeEnactmentTransactionId(string registerId, string proposalId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"governance-enactment-{registerId}-{proposalId}"))).ToLowerInvariant();

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private GovernanceApprovalActionPayload? TryDecodeApproval(TransactionModel tx)
    {
        try
        {
            var data = tx.Payloads.Length > 0 ? tx.Payloads[0].Data : null;
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            var bytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                ? Convert.FromBase64String(data)
                : Base64Url.DecodeFromChars(data);

            // The SAME options the payload was written with. Reading it back with ad-hoc options
            // throws on the kebab-case enums and the catch below swallows it as "not an approval",
            // so every approval silently stops counting and nothing is ever enacted.
            return JsonSerializer.Deserialize<GovernanceApprovalActionPayload>(
                bytes, GovernanceApprovalActionPayload.CanonicalJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            _logger.LogDebug(ex, "Could not decode transaction {TxId} as a governance approval", tx.TxId);
            return null;
        }
    }

    private GovernanceEnactmentResult Result(EnactmentOutcome outcome, string? txId, string detail)
    {
        if (outcome is not (EnactmentOutcome.Submitted or EnactmentOutcome.QuorumNotMet
            or EnactmentOutcome.AlreadyEnacted or EnactmentOutcome.CannotCarry))
        {
            _logger.LogWarning("Enactment attempt ended {Outcome}: {Detail}", outcome, detail);
        }

        return new GovernanceEnactmentResult(outcome, txId, detail);
    }
}
