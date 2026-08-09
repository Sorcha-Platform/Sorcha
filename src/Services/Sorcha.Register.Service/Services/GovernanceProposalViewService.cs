// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>One organisation's approval, individually attributed (T046).</summary>
/// <param name="ApproverDid">The organisation that approved.</param>
/// <param name="IsApproval">Approve, or reject.</param>
/// <param name="ApprovedAt">When the approval transaction was recorded.</param>
/// <param name="TxId">The approval's own transaction — the evidence, not a summary of it.</param>
/// <param name="AuthMethod">How the approving key was held. Recorded, never enforced (R-016).</param>
/// <param name="AccountableIndividualDid">Who stands behind it, when the authorisation names one.</param>
public sealed record GovernanceApprovalView(
    string ApproverDid,
    bool IsApproval,
    DateTimeOffset ApprovedAt,
    string TxId,
    string AuthMethod,
    string? AccountableIndividualDid);

/// <summary>An approval on the ledger that cannot count, with the reason (FR-011c).</summary>
/// <param name="ApproverDid">Who it claimed to be from.</param>
/// <param name="TxId">Its transaction, so an auditor can read it for themselves.</param>
/// <param name="Reason">Why it was excluded.</param>
public sealed record GovernanceExcludedApprovalView(
    string ApproverDid,
    string? TxId,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ApprovalTallyRefusal Reason);

/// <summary>A proposal as an operator sees it.</summary>
/// <remarks>
/// <b>Every enum here is annotated to serialise as a name.</b> The Register Service configures no JSON
/// options, so its minimal APIs use the web defaults — under which an enum goes on the wire as a
/// NUMBER. A status filter written against <c>Enacted</c> would never match <c>1</c>, and a typed
/// client reading a string throws outright. Pinning it on the type rather than on the host means the
/// shape cannot change because some other registration did.
/// </remarks>
public sealed record GovernanceProposalView
{
    /// <summary>The proposal's transaction id. The proposal <i>is</i> its id.</summary>
    public required string ProposalId { get; init; }

    /// <summary>What is being proposed.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required GovernanceOperationType OperationType { get; init; }

    /// <summary>The organisation that raised it.</summary>
    public required string ProposedBy { get; init; }

    /// <summary>Who or what the operation targets.</summary>
    public string? TargetDid { get; init; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset ProposedAt { get; init; }

    /// <summary>When its approval window closes. Absent when it has none.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The roster head it was raised against (FR-011a).</summary>
    public string? RosterSnapshotId { get; init; }

    /// <summary>
    /// The rule captured at raise time, so a later change cannot move the bar. Nullable because a
    /// proposal that captured none must not appear to claim one.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public QuorumFormula? QuorumFormula { get; init; }

    /// <summary>Derived, never stored.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required GovernanceProposalState Status { get; init; }

    /// <summary>Why it reached that state. Present on every terminal state.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required GovernanceProposalStateReason StatusReason { get; init; }

    /// <summary>The transaction that settled it, when one has.</summary>
    public string? OutcomeTxId { get; init; }

    /// <summary>How many approvals the captured formula requires.</summary>
    public int ApprovalsRequired { get; init; }

    /// <summary>How many are structurally eligible to count.</summary>
    public int ApprovalsReceived { get; init; }

    /// <summary>Each approval, attributed. Empty on the list surface.</summary>
    public IReadOnlyList<GovernanceApprovalView> Approvals { get; init; } = [];

    /// <summary>Approvals on the ledger that cannot count, with reasons. Empty on the list surface.</summary>
    public IReadOnlyList<GovernanceExcludedApprovalView> ExcludedApprovals { get; init; } = [];
}

/// <summary>Reads governance proposals for the audit surface.</summary>
public interface IGovernanceProposalViewService
{
    /// <summary>Lists the register's proposals, newest first, optionally filtered by derived state.</summary>
    Task<IReadOnlyList<GovernanceProposalView>> ListAsync(
        string registerId, GovernanceProposalState? state, CancellationToken ct = default);

    /// <summary>Full audit detail for one proposal, or <c>null</c> when the register carries no such proposal.</summary>
    Task<GovernanceProposalView?> GetAsync(
        string registerId, string proposalId, CancellationToken ct = default);
}

/// <summary>
/// The governance audit surface (T043/T046).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every field is derived from sealed content.</b> Status comes from
/// <see cref="GovernanceProposalStatus.Derive"/>, the approvals are the approval transactions
/// themselves, the counting is <see cref="GovernanceApprovalTally"/>'s and the arithmetic is
/// <c>ValidateQuorumAsync</c>'s. Nothing is stored and nothing is reimplemented — a second copy of
/// either would be a second answer to "is this proposal authorised", and the two would diverge
/// silently because nothing compares them.
/// </para>
/// <para>
/// <b>It reads payloads, not tracking metadata.</b> The endpoint this replaces reported
/// <c>operationType</c>, <c>proposerDid</c> and <c>targetDid</c> out of
/// <c>MetaData.TrackingData</c> — which sits outside the signature, outside the payload hash and
/// outside the docket's merkle leaf, so anyone able to submit can rewrite it with nothing detecting
/// the change. An audit surface sourced from forgeable fields is worse than none, because it looks
/// authoritative.
/// </para>
/// <para>
/// <b>The counts are structural.</b> Signature verification is the Validator's, deliberately: one
/// crypto loop, one authority. So <see cref="GovernanceProposalView.ApprovalsReceived"/> is an upper
/// bound — the approvals that <i>may</i> count — and a proposal showing enough of them may still not
/// enact if one of those signatures fails. That is why the state comes from whether an enactment
/// exists, never from the count.
/// </para>
/// </remarks>
public sealed class GovernanceProposalViewService : IGovernanceProposalViewService
{
    private readonly IGovernanceProposalReader _reader;
    private readonly IGovernanceRosterService _rosterService;
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initialises a new instance of the <see cref="GovernanceProposalViewService"/> class.</summary>
    public GovernanceProposalViewService(
        IGovernanceProposalReader reader,
        IGovernanceRosterService rosterService,
        IReadOnlyRegisterRepository repository,
        TimeProvider? timeProvider = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GovernanceProposalView>> ListAsync(
        string registerId, GovernanceProposalState? state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var roster = await _rosterService.GetCurrentRosterAsync(registerId, ct);
        var proposals = await _reader.ListAllAsync(registerId, ct);
        var now = _timeProvider.GetUtcNow();

        var views = new List<GovernanceProposalView>();

        foreach (var proposal in proposals)
        {
            var view = await BuildAsync(registerId, proposal, roster, now, withDetail: false, ct);
            if (view is not null && (state is null || view.Status == state))
            {
                views.Add(view);
            }
        }

        // Newest first: an operator opening this wants what just happened, not the genesis.
        views.Reverse();
        return views;
    }

    /// <inheritdoc />
    public async Task<GovernanceProposalView?> GetAsync(
        string registerId, string proposalId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        var read = await _reader.ReadAsync(registerId, proposalId, ct);
        if (read.Outcome != ProposalReadOutcome.Found)
        {
            return null;
        }

        var roster = await _rosterService.GetCurrentRosterAsync(registerId, ct);

        return await BuildAsync(
            registerId, read, roster, _timeProvider.GetUtcNow(), withDetail: true, ct);
    }

    private async Task<GovernanceProposalView?> BuildAsync(
        string registerId,
        GovernanceProposalRead read,
        AdminRoster? roster,
        DateTimeOffset now,
        bool withDetail,
        CancellationToken ct)
    {
        if (read.Operation is not { } operation || read.Transaction is not { } tx)
        {
            return null;
        }

        // The enactment's id is deterministic in (register, proposal), so finding it is one lookup
        // rather than a scan — and it is the SAME identity GovernanceEnactmentService writes, so the
        // two cannot disagree about which transaction enacted which proposal.
        var enactmentTxId = GovernanceEnactmentService.ComputeEnactmentTransactionId(registerId, tx.TxId);
        var enactment = await _repository.GetTransactionAsync(registerId, enactmentTxId, ct);

        var outcome = GovernanceProposalStatus.Derive(
            operation, tx.TxId, roster?.LastControlTxId, enactment is null ? null : enactmentTxId, now);

        var view = new GovernanceProposalView
        {
            ProposalId = tx.TxId,
            OperationType = operation.OperationType,
            ProposedBy = operation.ProposerDid,
            TargetDid = operation.TargetDid,
            ProposedAt = operation.ProposedAt,
            ExpiresAt = operation.ExpiresAt == default ? null : operation.ExpiresAt,
            RosterSnapshotId = operation.RosterSnapshotId,
            QuorumFormula = operation.QuorumFormulaAtRaise,
            Status = outcome.State,
            StatusReason = outcome.Reason,
            OutcomeTxId = outcome.OutcomeTxId,
        };

        if (roster is null)
        {
            return view;
        }

        var (plan, approvalTxIds) = await ReadApprovalsAsync(registerId, tx.TxId, operation, roster, ct);

        // The arithmetic is ValidateQuorumAsync's (R-007) — never reimplemented here, or a console
        // could report a proposal as short of quorum that the Validator is about to enact.
        var votes = GovernanceApprovalTally.ToVotes(
            plan, plan.Checks.Select(c => c.ApproverDid).ToHashSet(StringComparer.Ordinal));
        var quorum = await _rosterService.ValidateQuorumAsync(registerId, operation, votes, ct);

        view = view with
        {
            ApprovalsRequired = quorum.VotesRequired,
            ApprovalsReceived = quorum.VotesReceived,
        };

        if (!withDetail)
        {
            return view;
        }

        return view with
        {
            Approvals =
            [
                .. plan.Checks.Select(c => new GovernanceApprovalView(
                    ApproverDid: c.ApproverDid,
                    IsApproval: c.IsApproval,
                    ApprovedAt: approvalTxIds.TryGetValue(c.ApproverDid, out var a) ? a.At : default,
                    TxId: approvalTxIds.TryGetValue(c.ApproverDid, out var t) ? t.TxId : string.Empty,
                    AuthMethod: c.AuthMethod.ToString(),
                    AccountableIndividualDid: c.Authorisation?.IndividualDid))
            ],
            ExcludedApprovals =
            [
                .. plan.Excluded.Select(e => new GovernanceExcludedApprovalView(
                    e.ApproverDid,
                    approvalTxIds.TryGetValue(e.ApproverDid, out var x) ? x.TxId : null,
                    e.Refusal))
            ],
        };
    }

    /// <summary>Reads the approvals sealed against a proposal and prepares the tally over them.</summary>
    private async Task<(ApprovalTallyPlan Plan, Dictionary<string, (string TxId, DateTimeOffset At)> ByApprover)>
        ReadApprovalsAsync(
            string registerId,
            string proposalId,
            GovernanceOperation operation,
            AdminRoster roster,
            CancellationToken ct)
    {
        // Approvals chain off the proposal they answer, so the predecessor link finds them.
        var successors = await _repository.GetTransactionsByPrevTxIdAsync(registerId, proposalId, ct);

        var payloads = new List<GovernanceApprovalActionPayload>();
        var byApprover = new Dictionary<string, (string, DateTimeOffset)>(StringComparer.Ordinal);

        foreach (var candidate in successors.OrderBy(t => t.DocketNumber ?? 0))
        {
            var approval = TryDecodeApproval(candidate);
            if (approval is null || !string.Equals(
                    approval.Type, GovernanceApprovalActionPayload.PayloadType, StringComparison.Ordinal))
            {
                continue;
            }

            payloads.Add(approval);

            // First vote from an organisation stands, matching the tally's own duplicate rule.
            if (!string.IsNullOrWhiteSpace(approval.ApproverDid))
            {
                byApprover.TryAdd(approval.ApproverDid, (candidate.TxId, candidate.TimeStamp));
            }
        }

        return (GovernanceApprovalTally.Prepare(registerId, operation, roster.ControlRecord, payloads),
                byApprover);
    }

    private static GovernanceApprovalActionPayload? TryDecodeApproval(TransactionModel tx)
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
                : System.Buffers.Text.Base64Url.DecodeFromChars(data);

            // The SAME options the payload was written with. Ad-hoc options throw on its kebab-case
            // enums, and a catch that treats a throw as "not an approval" is how every approval
            // silently stopped counting on the first live run.
            return System.Text.Json.JsonSerializer.Deserialize<GovernanceApprovalActionPayload>(
                bytes, GovernanceApprovalActionPayload.CanonicalJsonOptions);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            return null;
        }
    }
}
