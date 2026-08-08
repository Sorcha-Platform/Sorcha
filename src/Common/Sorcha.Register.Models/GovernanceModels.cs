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
    Transfer = 2,

    /// <summary>
    /// Add a validator's signing key to the validator roster
    /// </summary>
    AddValidator = 3,

    /// <summary>
    /// Revoke a validator's signing key from the validator roster
    /// </summary>
    RemoveValidator = 4,

    /// <summary>
    /// Rotate a validator's signing key (mark old as Rotated, add new Active key)
    /// </summary>
    RotateValidatorKey = 5,

    /// <summary>
    /// Change the register's cryptographic policy — notably promoting it from DevMode
    /// (plaintext payloads) to Normal (mandatory field-level encryption).
    /// </summary>
    /// <remarks>
    /// Feature 189 (FR-021). The promotion is one-way: validators refuse a crypto-policy update
    /// that re-enables DevMode, so this operation can tighten a register's posture but never
    /// loosen it.
    /// </remarks>
    /// <remarks>
    /// Value 8, not 6: <c>RevokeIssuanceKey</c> (6) and <c>RotateIssuanceKey</c> (7) were added by
    /// Feature 120 after <c>RotateValidatorKey</c> (5). C# permits duplicate enum values silently,
    /// so a collision here would make two distinct operations compare equal and switch to the same
    /// arm — with no compiler warning. <c>GovernanceOperationTypeTests</c> guards it.
    /// </remarks>
    CryptoPolicyUpdate = 8,

    /// <summary>
    /// Feature 120 US6 (VAL_CRED_GOV_001) — revoke an organisation's issuance key.
    /// After quorum approval, the wallet service marks the named rotation as
    /// <c>Revoked</c>, the published DID document drops it from
    /// <c>assertionMethod</c>, and verifiers reject credentials signed by it.
    /// </summary>
    RevokeIssuanceKey = 6,

    /// <summary>
    /// Feature 120 US6 — rotate an organisation's issuance key. Marks the
    /// existing Active key as <c>Rotated</c> and derives a new key at the next
    /// rotation index.
    /// </summary>
    RotateIssuanceKey = 7
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

    /// <summary>
    /// Validator roster entry for AddValidator/RotateValidatorKey operations.
    /// Contains the new validator's public key, algorithm, and derivation context.
    /// </summary>
    [JsonPropertyName("validatorEntry")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidatorRosterEntry? ValidatorEntry { get; set; }

    /// <summary>
    /// Identifier of the control transaction that established the roster this proposal was raised
    /// against (Feature 189, FR-011a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proposal is evaluated against the roster and quorum rule <b>as they stood when it was
    /// raised</b>, so neither the set of eligible approvers nor the number of approvals required can
    /// shift while it is open. Any enacted roster change makes the register's current
    /// roster-establishing transaction differ from this value, which <b>invalidates</b> the proposal
    /// (FR-011b).
    /// </para>
    /// <para>
    /// The alternative — counting against the live roster — would let a proposal become enactable
    /// purely because the pool shrank: under unanimity, removing a dissenting organisation would
    /// convert a blocked change into an enacted one, making roster removal an attack on every open
    /// proposal. Invalidation is a comparison, needing no timer and no sweeper, and it is
    /// deterministic on every node — which the Feature 145 projection requires.
    /// </para>
    /// </remarks>
    [JsonPropertyName("rosterSnapshotId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RosterSnapshotId { get; set; }

    /// <summary>
    /// The quorum rule in force when this proposal was raised, frozen so the requirement cannot
    /// change beneath it (Feature 189, FR-011a).
    /// </summary>
    [JsonPropertyName("quorumFormulaAtRaise")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuorumFormula? QuorumFormulaAtRaise { get; set; }
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
    /// How the approving organisation's key was held — <c>software</c>, <c>hardware-backed</c>,
    /// <c>service</c> or <c>unknown</c> (Feature 189 T081 / R-016).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Populated by <see cref="GovernanceApprovalTally.ToVotes"/> from the sealed approval's own
    /// <see cref="ApprovalAuthMethod"/>, in the same spelling that payload carries. Recorded so a
    /// register <i>can</i> later require a minimum standard per operation — never enforced here.
    /// Enforcing one before organisations have hardware-backed governance keys provisioned would
    /// lock them out of their own registers.
    /// </para>
    /// <para>
    /// <b>This field means key custody, not how a person authenticated.</b> It previously carried
    /// <c>passkey</c> / <c>totp</c> / <c>password</c> / <c>re-oauth</c>, written by the in-platform
    /// approval path that R-014 replaced with external signing — a path that had no callers left.
    /// That service is deleted rather than left registered, because one field carrying two
    /// vocabularies is a fact no consumer can interpret.
    /// </para>
    /// <para>
    /// Deliberately coarse. The challenge, its response and any device identifiers stay off the
    /// ledger: this record is replicated to every node and readable by every future auditor, so it
    /// carries the fact and not the evidence.
    /// </para>
    /// </remarks>
    [JsonPropertyName("authMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthMethod { get; set; }

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
    /// Full roster snapshot as of this Control transaction, or <c>null</c> when the transaction
    /// changes no roster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null is load-bearing (Feature 189).</b> A <i>pending</i> governance proposal enacts
    /// nothing — it is raised so approvals have something to attach to — so it must not appear to
    /// roster reconstruction as a roster change. <c>GovernanceRosterService.GetCurrentRosterAsync</c>
    /// walks control transactions newest-first and skips any whose payload carries no populated
    /// roster, so a null here keeps <c>LastControlTxId</c> on the last <i>real</i> roster commit.
    /// </para>
    /// <para>
    /// That matters because FR-011b invalidates a proposal whose <c>RosterSnapshotId</c> no longer
    /// matches <c>LastControlTxId</c>. A proposal that wrote a roster would move that value and
    /// invalidate <b>itself</b> the moment it sealed — which is exactly what the pre-split
    /// propose-and-enact transaction did, and why no approval could ever attach to one.
    /// </para>
    /// </remarks>
    [JsonPropertyName("roster")]
    public RegisterControlRecord? Roster { get; set; }

    /// <summary>
    /// The governance operation that produced this roster state.
    /// Null for genesis Control transactions.
    /// </summary>
    [JsonPropertyName("operation")]
    public GovernanceOperation? Operation { get; set; }

    /// <summary>
    /// Transaction id of the proposal this transaction enacts, or <c>null</c> when it is not a
    /// separate enactment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set only on an <b>enactment</b> raised after approvals were collected against a pending
    /// proposal. It tells the Validator where to find the approvals that authorise the change, so
    /// quorum is counted from <b>sealed content</b> rather than from a list the enacting payload
    /// carries — which two nodes could assemble differently for the same proposal.
    /// </para>
    /// <para>
    /// It lives on the envelope rather than on <see cref="GovernanceOperation"/> deliberately: the
    /// approval digest binds the operation's whole serialisation, so adding a field there would change
    /// what every stored approval signature covers. The envelope is outside that digest.
    /// </para>
    /// <para>
    /// Null on an Owner-override propose-and-enact, which stays a single transaction carrying its own
    /// approvals inline — so that path's validation is untouched.
    /// </para>
    /// </remarks>
    [JsonPropertyName("enactsProposalId")]
    public string? EnactsProposalId { get; set; }
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
