// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Types of governance operations that can be performed on a register's admin roster
/// </summary>
public enum GovernanceOperationType
{
    /// <summary>
    /// Add a new member to the roster
    /// </summary>
    Add = 0,

    /// <summary>
    /// Remove an existing member from the roster
    /// </summary>
    Remove = 1,

    /// <summary>
    /// Transfer ownership to an existing admin
    /// </summary>
    Transfer = 2
}

/// <summary>
/// Status of a governance proposal
/// </summary>
public enum ProposalStatus
{
    /// <summary>
    /// Awaiting quorum votes or target acceptance
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Quorum reached, awaiting target acceptance
    /// </summary>
    Approved = 1,

    /// <summary>
    /// Quorum blocked or target declined
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// 7-day timeout reached without resolution
    /// </summary>
    Expired = 3,

    /// <summary>
    /// Control transaction successfully written to register
    /// </summary>
    Recorded = 4
}

/// <summary>
/// Represents a governance operation (add, remove, or transfer) on a register's admin roster
/// </summary>
public class GovernanceOperation
{
    /// <summary>
    /// The type of governance operation
    /// </summary>
    [Required]
    [JsonPropertyName("operationType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GovernanceOperationType OperationType { get; set; }

    /// <summary>
    /// DID of the admin who proposed this operation
    /// </summary>
    [Required]
    [JsonPropertyName("proposerDid")]
    public string ProposerDid { get; set; } = string.Empty;

    /// <summary>
    /// DID of the target member (being added, removed, or receiving ownership)
    /// </summary>
    [Required]
    [JsonPropertyName("targetDid")]
    public string TargetDid { get; set; } = string.Empty;

    /// <summary>
    /// Role being assigned to the target (for Add/Transfer operations)
    /// </summary>
    [Required]
    [JsonPropertyName("targetRole")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisterRole TargetRole { get; set; }

    /// <summary>
    /// Signatures from voting members who approved the operation
    /// </summary>
    [JsonPropertyName("approvalSignatures")]
    public List<ApprovalSignature> ApprovalSignatures { get; set; } = new();

    /// <summary>
    /// When the proposal was created (UTC)
    /// </summary>
    [Required]
    [JsonPropertyName("proposedAt")]
    public DateTimeOffset ProposedAt { get; set; }

    /// <summary>
    /// When the proposal expires (ProposedAt + 7 days)
    /// </summary>
    [Required]
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Current status of the proposal
    /// </summary>
    [Required]
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ProposalStatus Status { get; set; }

    /// <summary>
    /// Optional justification for the governance operation
    /// </summary>
    [StringLength(500)]
    [JsonPropertyName("justification")]
    public string? Justification { get; set; }
}

/// <summary>
/// A voting approval signature from a roster member
/// </summary>
public class ApprovalSignature
{
    /// <summary>
    /// DID of the approver
    /// </summary>
    [Required]
    [JsonPropertyName("approverDid")]
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded signature
    /// </summary>
    [Required]
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is an approval (true) or rejection (false)
    /// </summary>
    [JsonPropertyName("isApproval")]
    public bool IsApproval { get; set; }

    /// <summary>
    /// When the vote was cast (UTC)
    /// </summary>
    [Required]
    [JsonPropertyName("votedAt")]
    public DateTimeOffset VotedAt { get; set; }

    /// <summary>
    /// Optional comment with the vote
    /// </summary>
    [StringLength(500)]
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

/// <summary>
/// Payload of a Control transaction — contains the full current roster and the operation that produced it
/// </summary>
public class ControlTransactionPayload
{
    /// <summary>
    /// Payload schema version
    /// </summary>
    [Required]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Full current roster snapshot at the time of this Control transaction
    /// </summary>
    [Required]
    [JsonPropertyName("roster")]
    public RegisterControlRecord Roster { get; set; } = new();

    /// <summary>
    /// The governance operation that produced this roster state.
    /// Null for genesis Control transactions.
    /// </summary>
    [JsonPropertyName("operation")]
    public GovernanceOperation? Operation { get; set; }
}

/// <summary>
/// Formula used to calculate the quorum threshold for governance proposals
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuorumFormula
{
    /// <summary>
    /// Strict majority: floor(m/2) + 1
    /// </summary>
    StrictMajority = 0,

    /// <summary>
    /// Supermajority: floor(2*m/3) + 1
    /// </summary>
    Supermajority = 1,

    /// <summary>
    /// Unanimous: m (all voting members must approve)
    /// </summary>
    Unanimous = 2
}

/// <summary>
/// Controls how new validators join a register's peer network
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegistrationMode
{
    /// <summary>
    /// Any validator can register without approval
    /// </summary>
    Public = 0,

    /// <summary>
    /// Only approved validators can register (requires admin consent)
    /// </summary>
    Consent = 1
}

/// <summary>
/// Mechanism used to elect the next block proposer among validators
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ElectionMechanism
{
    /// <summary>
    /// Round-robin rotation among validators (current implementation)
    /// </summary>
    Rotating = 0,

    /// <summary>
    /// Raft consensus leader election (future)
    /// </summary>
    Raft = 1,

    /// <summary>
    /// Stake-weighted election based on validator stake (future)
    /// </summary>
    StakeWeighted = 2
}

/// <summary>
/// Controls how unapproved validators are handled during a public-to-consent mode transition
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransitionMode
{
    /// <summary>
    /// Unapproved validators are ejected immediately at commit
    /// </summary>
    Immediate = 0,

    /// <summary>
    /// Unapproved validators continue operating for one TTL cycle before ejection
    /// </summary>
    GracePeriod = 1
}
