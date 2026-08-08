// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// Where a governance proposal stands, derived from sealed content (T043/T046).
/// </summary>
/// <remarks>
/// <b>Deliberately not <see cref="ProposalStatus"/>.</b> That enum is the value stored <i>inside</i> a
/// proposal's payload at the moment it was raised — <c>Pending</c> on a proposal, <c>Recorded</c> on an
/// enactment — and it never changes afterwards, because a sealed transaction is immutable. This is the
/// live answer, and folding the two into one type would leave a name that means "what was written" in
/// some places and "where it stands now" in others, with nothing to tell a reader which they hold.
/// </remarks>
public enum GovernanceProposalState
{
    /// <summary>Still actionable: awaiting approvals, on the roster it was raised against.</summary>
    Open = 0,

    /// <summary>An enactment for it is on the ledger. Terminal, and outranks every other state.</summary>
    Enacted = 1,

    /// <summary>The roster moved on beneath it, so it can no longer be enacted (FR-011b).</summary>
    Invalidated = 2,

    /// <summary>Its window passed without an enactment.</summary>
    Expired = 3,
}

/// <summary>
/// Why a proposal reached the state it did. Every terminal state carries one (FR-011c).
/// </summary>
/// <remarks>
/// <para>
/// <b>There is deliberately no <c>Withdrawn</c>.</b> Nothing on the platform can withdraw a proposal —
/// there is no endpoint, no transaction type and no producer of any kind — so a status for it would be
/// a state no ledger can ever reach, reported by a surface that looks complete. The contract lists it
/// as a filter value; it is absent here until something can produce it.
/// </para>
/// <para>
/// <b>And no <c>refused-not-on-roster</c>.</b> That is why an individual <i>approval</i> did not count,
/// not what happened to the proposal — a proposal whose every approval was refused is still Open. It
/// belongs per-approval, where <see cref="ApprovalTallyRefusal"/> already carries it.
/// </para>
/// </remarks>
public enum GovernanceProposalStateReason
{
    /// <summary>Not terminal.</summary>
    None = 0,

    /// <summary>Enough organisations approved and the change was enacted.</summary>
    QuorumMet = 1,

    /// <summary>The register's roster head moved past the snapshot the proposal was raised against.</summary>
    RosterChanged = 2,

    /// <summary>The approval window closed with no enactment.</summary>
    Expired = 3,
}

/// <summary>Where a proposal stands, and how it got there.</summary>
/// <param name="State">The derived state.</param>
/// <param name="Reason">Why, for every terminal state.</param>
/// <param name="OutcomeTxId">The transaction that settled it, when one has.</param>
public readonly record struct GovernanceProposalOutcome(
    GovernanceProposalState State,
    GovernanceProposalStateReason Reason,
    string? OutcomeTxId);

/// <summary>
/// Derives a proposal's status from sealed content alone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is stored.</b> A status column would be a second source of truth about a fact the
/// ledger already carries, and it would drift the moment one node folds a docket the writer never saw
/// — which is the ordinary case in a federated deployment, not an edge one. The same inputs give the
/// same answer on every node, so a console, the CLI and a service cannot disagree about where a
/// proposal stands.
/// </para>
/// <para>
/// It lives in this zero-dependency leaf for that reason: the way independent readers come to disagree
/// is by each writing their own derivation.
/// </para>
/// </remarks>
public static class GovernanceProposalStatus
{
    /// <summary>
    /// Derives where a proposal stands.
    /// </summary>
    /// <param name="operation">The operation stored on the proposal transaction.</param>
    /// <param name="proposalTxId">The proposal's own transaction id — the proposal <i>is</i> its id.</param>
    /// <param name="currentRosterHead">
    /// The register's current <c>LastControlTxId</c>. Compared against the proposal's snapshot, which
    /// is the comparison FR-011b enforces at count time — never a sweeper, never a timer.
    /// </param>
    /// <param name="enactmentTxId">
    /// The separate enactment transaction for this proposal, or <c>null</c> when none exists.
    /// </param>
    /// <param name="now">
    /// Evaluation time, passed in so the result is deterministic and testable rather than reading the
    /// ambient clock.
    /// </param>
    public static GovernanceProposalOutcome Derive(
        GovernanceOperation operation,
        string proposalTxId,
        string? currentRosterHead,
        string? enactmentTxId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalTxId);

        // ENACTED OUTRANKS EVERYTHING, and the order below is load-bearing twice over.
        //
        // An enactment IS a roster change, so the head has ALWAYS moved past an enacted proposal's
        // snapshot — checking invalidation first would report every enacted proposal as Invalidated.
        // And checking expiry first would make an enacted proposal silently re-read as Expired once
        // its window passed, contradicting the ledger it is reporting. Both would look correct for as
        // long as anyone tested inside the window.
        if (!string.IsNullOrEmpty(enactmentTxId))
        {
            return new GovernanceProposalOutcome(
                GovernanceProposalState.Enacted, GovernanceProposalStateReason.QuorumMet, enactmentTxId);
        }

        // The Owner override writes ONE propose-and-enact transaction, so the proposal is its own
        // enactment and there is no separate transaction to look for.
        if (operation.Status == ProposalStatus.Recorded)
        {
            return new GovernanceProposalOutcome(
                GovernanceProposalState.Enacted, GovernanceProposalStateReason.QuorumMet, proposalTxId);
        }

        // Reported ahead of expiry: it refuses an approval even inside the window, so it is the
        // binding constraint. A proposal that captured no snapshot cannot be judged against a head —
        // the same carve-out the enactment path applies.
        if (!string.IsNullOrEmpty(operation.RosterSnapshotId)
            && !string.Equals(operation.RosterSnapshotId, currentRosterHead, StringComparison.Ordinal))
        {
            return new GovernanceProposalOutcome(
                GovernanceProposalState.Invalidated, GovernanceProposalStateReason.RosterChanged, null);
        }

        // `!= default` matters: treating an unset window as a date at the epoch reports EVERY proposal
        // as expired. The approve endpoint guards it the same way.
        if (operation.ExpiresAt != default && operation.ExpiresAt < now)
        {
            return new GovernanceProposalOutcome(
                GovernanceProposalState.Expired, GovernanceProposalStateReason.Expired, null);
        }

        return new GovernanceProposalOutcome(
            GovernanceProposalState.Open, GovernanceProposalStateReason.None, null);
    }
}
