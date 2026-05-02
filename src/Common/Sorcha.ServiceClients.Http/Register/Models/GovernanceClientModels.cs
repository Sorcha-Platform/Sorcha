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
/// Summary of a governance proposal from transaction history
/// </summary>
public class GovernanceProposalSummary
{
    /// <summary>Identifier of the tx.</summary>
    public string? TxId { get; set; }
    /// <summary>Numeric value for docket number.</summary>
    public long? DocketNumber { get; set; }
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset? TimeStamp { get; set; }
    /// <summary>The operation type.</summary>
    public string? OperationType { get; set; }
    /// <summary>Identifier of the proposer did.</summary>
    public string? ProposerDid { get; set; }
    /// <summary>Identifier of the target did.</summary>
    public string? TargetDid { get; set; }
}
