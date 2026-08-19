// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.ServiceClients.Register.Models;

/// <summary>
/// Request to submit a governance proposal
/// </summary>
public class GovernanceProposalRequest
{
    /// <summary>The operation type.</summary>
    public GovernanceOperationType OperationType { get; set; }
    /// <summary>Identifier of the proposer did.</summary>
    public string ProposerDid { get; set; } = string.Empty;
    /// <summary>Identifier of the target did.</summary>
    public string TargetDid { get; set; } = string.Empty;
    /// <summary>The target role.</summary>
    public RegisterRole? TargetRole { get; set; }
    /// <summary>The justification.</summary>
    public string? Justification { get; set; }
    /// <summary>Collection of approval signatures associated with this resource.</summary>
    public List<ApprovalSignature>? ApprovalSignatures { get; set; }

    /// <summary>
    /// The target organisation's signed acceptance of the seat (Feature 193). Required for
    /// <see cref="GovernanceOperationType.Add"/>: it nominates the slot-100 governance key to record
    /// on the roster and proves the organisation holds it. Without one the member is seated unkeyed
    /// and can never sign (#1464).
    /// </summary>
    public GovernanceSeatAcceptance? TargetAcceptance { get; set; }
}

/// <summary>
/// Response from a governance proposal submission
/// </summary>
public class GovernanceProposalResponse
{
    /// <summary>Identifier of the tx.</summary>
    public string TxId { get; set; } = string.Empty;
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>The operation type.</summary>
    public string OperationType { get; set; } = string.Empty;
    /// <summary>Identifier of the proposer did.</summary>
    public string ProposerDid { get; set; } = string.Empty;
    /// <summary>Identifier of the target did.</summary>
    public string TargetDid { get; set; } = string.Empty;
    /// <summary>The target role.</summary>
    public string TargetRole { get; set; } = string.Empty;
    /// <summary>Flag indicating submitted.</summary>
    public bool Submitted { get; set; }
}

/// <summary>
/// Paginated list of governance proposals
/// </summary>
public class GovernanceProposalPage
{
    /// <summary>One-based page number for paginated results.</summary>
    public int Page { get; set; }
    /// <summary>Number of items per page.</summary>
    public int PageSize { get; set; }
    /// <summary>Total number of items available.</summary>
    public int Total { get; set; }
    /// <summary>Collection of proposals associated with this resource.</summary>
    public List<GovernanceProposalSummary> Proposals { get; set; } = [];
}

/// <summary>
/// Summary of a governance proposal, with its derived status (Feature 189 T046).
/// </summary>
/// <remarks>
/// <para>
/// <b>These names must match <c>GovernanceProposalView</c> on the server.</b> The endpoint used to
/// report <c>txId</c> / <c>docketNumber</c> / <c>timeStamp</c> / <c>proposerDid</c> out of tracking
/// metadata; it now reports the signed payload as <c>proposalId</c> / <c>proposedBy</c> and adds the
/// derived status. Leaving the old names here would not have failed anything: System.Text.Json
/// ignores properties it cannot match, so every proposal would have deserialised to a row of nulls
/// and an empty status — a list that renders as "no proposals" while the register is full of them.
/// </para>
/// <para>
/// Status and reason are strings rather than the server enums because this client is referenced by
/// projects that do not take <c>Sorcha.Register.Models</c>. The wire values are the enum names.
/// </para>
/// </remarks>
public class GovernanceProposalSummary
{
    /// <summary>The proposal's transaction id. The proposal <i>is</i> its id.</summary>
    public string? ProposalId { get; set; }
    /// <summary>The operation type.</summary>
    public string? OperationType { get; set; }
    /// <summary>The organisation that raised it.</summary>
    public string? ProposedBy { get; set; }
    /// <summary>Identifier of the target did.</summary>
    public string? TargetDid { get; set; }
    /// <summary>When it was raised.</summary>
    public DateTimeOffset? ProposedAt { get; set; }
    /// <summary>When its approval window closes, when it has one.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    /// <summary>The roster head it was raised against.</summary>
    public string? RosterSnapshotId { get; set; }
    /// <summary>The quorum rule captured at raise time.</summary>
    public string? QuorumFormula { get; set; }
    /// <summary>Derived on every read, never stored: Open, Enacted, Invalidated or Expired.</summary>
    public string? Status { get; set; }
    /// <summary>Why it reached that state. Present on every terminal state.</summary>
    public string? StatusReason { get; set; }
    /// <summary>The transaction that settled it, when one has.</summary>
    public string? OutcomeTxId { get; set; }
    /// <summary>How many approvals the captured formula requires.</summary>
    public int ApprovalsRequired { get; set; }
    /// <summary>How many are structurally eligible to count.</summary>
    public int ApprovalsReceived { get; set; }
}
