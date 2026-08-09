// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Client-side shapes for the governance audit surface (Feature 189 T046/T084).
/// </summary>
/// <remarks>
/// <para>
/// Every field here is <b>reported</b> by the Register Service, never computed in the browser. That is
/// the whole point of T084: an approver must read the change that will actually be written, and a
/// console deriving its own preview would eventually show them an accurate-looking change that differs
/// from the one that happens — FR-027 defeated more quietly than by showing a JSON blob.
/// </para>
/// <para>
/// The enum-valued fields are <c>string</c> because the wire carries enum <i>names</i>, and a client
/// enum would silently bind to its own default for any value the server later adds. Compare against
/// the constants below rather than against literals scattered through markup.
/// </para>
/// </remarks>
public sealed class GovernanceProposalSummaryViewModel
{
    /// <summary>The proposal's transaction id. The proposal <i>is</i> its id.</summary>
    public string ProposalId { get; set; } = string.Empty;

    /// <summary>What is being proposed — Add, Remove, Transfer, CryptoPolicyUpdate, …</summary>
    public string? OperationType { get; set; }

    /// <summary>The organisation that raised it.</summary>
    public string? ProposedBy { get; set; }

    /// <summary>Who or what the operation targets.</summary>
    public string? TargetDid { get; set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset? ProposedAt { get; set; }

    /// <summary>When the approval window closes, when it has one.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>The roster head it was raised against.</summary>
    public string? RosterSnapshotId { get; set; }

    /// <summary>The quorum rule captured at raise time, so a later change cannot move the bar.</summary>
    public string? QuorumFormula { get; set; }

    /// <summary>Derived from sealed content on every read: Open, Enacted, Invalidated or Expired.</summary>
    public string? Status { get; set; }

    /// <summary>Why it reached that state. Present on every terminal state.</summary>
    public string? StatusReason { get; set; }

    /// <summary>The transaction that settled it, when one has.</summary>
    public string? OutcomeTxId { get; set; }

    /// <summary>How many approvals the captured formula requires.</summary>
    public int ApprovalsRequired { get; set; }

    /// <summary>
    /// How many are structurally eligible to count.
    /// </summary>
    /// <remarks>
    /// An <b>upper bound</b>: signature verification is the Validator's, so a proposal showing enough
    /// approvals may still not enact if one of those signatures fails. Never present it as "will
    /// enact" — the status is what says whether it did.
    /// </remarks>
    public int ApprovalsReceived { get; set; }

    /// <summary>Each approval, attributed. Populated on the detail read only.</summary>
    public List<GovernanceApprovalViewModel> Approvals { get; set; } = [];

    /// <summary>Approvals on the ledger that cannot count, with reasons. Detail read only.</summary>
    public List<GovernanceExcludedApprovalViewModel> ExcludedApprovals { get; set; } = [];

    /// <summary>
    /// The roster as it would be if this proposal enacted. <c>null</c> when there is nothing truthful
    /// to show — see <see cref="GovernanceProposalStates"/>.
    /// </summary>
    public List<GovernanceRosterMemberViewModel>? RosterDiff { get; set; }
}

/// <summary>One organisation's approval, individually attributed.</summary>
public sealed class GovernanceApprovalViewModel
{
    /// <summary>The organisation that approved.</summary>
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>Approve, or reject.</summary>
    public bool IsApproval { get; set; }

    /// <summary>When the approval transaction was recorded.</summary>
    public DateTimeOffset ApprovedAt { get; set; }

    /// <summary>The approval's own transaction — the evidence, not a summary of it.</summary>
    public string TxId { get; set; } = string.Empty;

    /// <summary>How the approving key was held. Recorded, never enforced.</summary>
    public string? AuthMethod { get; set; }

    /// <summary>The person who stands behind it.</summary>
    public string? AccountableIndividualDid { get; set; }
}

/// <summary>An approval on the ledger that cannot count, with the reason.</summary>
public sealed class GovernanceExcludedApprovalViewModel
{
    /// <summary>Who it claimed to be from.</summary>
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>Its transaction, so an auditor can read it for themselves.</summary>
    public string? TxId { get; set; }

    /// <summary>Why it was excluded — NotOnRoster, KeyNotTheRosterKey, DuplicateApprover, …</summary>
    public string? Reason { get; set; }
}

/// <summary>One row of the roster diff.</summary>
public sealed class GovernanceRosterMemberViewModel
{
    /// <summary>The organisation, as a roster subject.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The role it holds after the change — for a removal, the role it is losing.</summary>
    public string? Role { get; set; }

    /// <summary>Unchanged, Added, Removed or RoleChanged.</summary>
    public string? Change { get; set; }
}

/// <summary>Paged proposals, as the list endpoint returns them.</summary>
public sealed class GovernanceProposalPageViewModel
{
    /// <summary>One-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Total matching the filter.</summary>
    public int Total { get; set; }

    /// <summary>The proposals.</summary>
    public List<GovernanceProposalSummaryViewModel> Proposals { get; set; } = [];
}

/// <summary>The derived states, as the server names them on the wire.</summary>
/// <remarks>
/// Constants rather than literals in markup: a status the renderer does not recognise falls through
/// to a neutral presentation, and a typo in one branch would silently take that path for ever.
/// </remarks>
public static class GovernanceProposalStates
{
    /// <summary>Still actionable, on the roster it was raised against.</summary>
    public const string Open = "Open";

    /// <summary>An enactment for it is on the ledger.</summary>
    public const string Enacted = "Enacted";

    /// <summary>The roster moved on beneath it, so it can no longer be enacted.</summary>
    public const string Invalidated = "Invalidated";

    /// <summary>Its window passed without an enactment.</summary>
    public const string Expired = "Expired";
}

/// <summary>The roster-diff change markers, as the server names them.</summary>
public static class GovernanceRosterChanges
{
    /// <summary>On the roster before and after, in the same role.</summary>
    public const string Unchanged = "Unchanged";

    /// <summary>Joins the roster if this enacts.</summary>
    public const string Added = "Added";

    /// <summary>Leaves the roster if this enacts.</summary>
    public const string Removed = "Removed";

    /// <summary>Stays, in a different role.</summary>
    public const string RoleChanged = "RoleChanged";
}
