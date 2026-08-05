// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// A signed approval or rejection of a proposed docket by a validator — the evidence that a docket
/// achieved (or failed) quorum. Persisted to the register alongside the docket it votes on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moved here from <c>Sorcha.Validator.Service.Models.ConsensusVote</c> (Feature 187 / #1371).</b>
/// It is not a validator working model and not a superset of some smaller ledger shape: every member
/// is immutable evidence — who voted, on which docket, which way, why (when rejecting), when, and
/// the signature proving it. There is no transient state of the kind the validator's genuinely-local
/// types carry (<c>Transaction.Priority</c>/<c>AddedToPoolAt</c>/<c>RetryCount</c>,
/// <c>Docket.Status</c>). Defining it in one consumer's assembly is what kept it off the ledger.
/// </para>
/// <para>
/// <b>Why one declaration matters here.</b> Consensus votes are the record that a docket achieved
/// quorum. Before Feature 187 they were never persisted at all, so "this docket was agreed by these
/// validators, and here are their signatures" was not recoverable from the register — on a platform
/// whose proposition is an immutable audit record. A second, drifting copy of this shape would
/// reintroduce that gap quietly; see <see cref="VoteDecision"/> for what a divergent copy already
/// cost (two declarations whose <c>Reject</c> values disagreed, 2 versus 0).
/// </para>
/// </remarks>
public class ConsensusVote
{
    /// <summary>
    /// Unique vote identifier.
    /// </summary>
    public required string VoteId { get; init; }

    /// <summary>
    /// Identifier of the docket being voted on.
    /// </summary>
    public required string DocketId { get; init; }

    /// <summary>
    /// Identifier of the validator casting this vote.
    /// </summary>
    public required string ValidatorId { get; init; }

    /// <summary>
    /// Approve, reject, or abstain. Never <see cref="VoteDecision.Unspecified"/> on a real vote —
    /// consumers must reject that value explicitly rather than relying on <c>Enum.IsDefined</c>.
    /// </summary>
    public required VoteDecision Decision { get; init; }

    /// <summary>
    /// Reason for rejection. Required when <see cref="Decision"/> is
    /// <see cref="VoteDecision.Reject"/>.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// When the vote was cast.
    /// </summary>
    public required DateTimeOffset VotedAt { get; init; }

    /// <summary>
    /// The validator's cryptographic signature over the vote.
    /// </summary>
    public required RegisterSignature ValidatorSignature { get; init; }

    /// <summary>
    /// Hash of the docket being voted on, so the vote can be verified against the docket it claims
    /// to cover.
    /// </summary>
    public required string DocketHash { get; init; }
}
