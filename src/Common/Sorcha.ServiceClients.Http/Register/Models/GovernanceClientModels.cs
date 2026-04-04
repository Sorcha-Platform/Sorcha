// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.ServiceClients.Register.Models;

/// <summary>
/// Request to submit a governance proposal
/// </summary>
public class GovernanceProposalRequest
{
    public GovernanceOperationType OperationType { get; set; }
    public string ProposerDid { get; set; } = string.Empty;
    public string TargetDid { get; set; } = string.Empty;
    public RegisterRole? TargetRole { get; set; }
    public string? Justification { get; set; }
    public List<ApprovalSignature>? ApprovalSignatures { get; set; }
}

/// <summary>
/// Response from a governance proposal submission
/// </summary>
public class GovernanceProposalResponse
{
    public string TxId { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string ProposerDid { get; set; } = string.Empty;
    public string TargetDid { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public bool Submitted { get; set; }
}

/// <summary>
/// Paginated list of governance proposals
/// </summary>
public class GovernanceProposalPage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public List<GovernanceProposalSummary> Proposals { get; set; } = [];
}

/// <summary>
/// Summary of a governance proposal from transaction history
/// </summary>
public class GovernanceProposalSummary
{
    public string? TxId { get; set; }
    public long? DocketNumber { get; set; }
    public DateTimeOffset? TimeStamp { get; set; }
    public string? OperationType { get; set; }
    public string? ProposerDid { get; set; }
    public string? TargetDid { get; set; }
}
