// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Payload of a <c>TransactionType.CredentialStatusChange</c> register transaction —
/// carries an issuer-driven credential lifecycle event (Revoke / Suspend / Reinstate)
/// that the recipient's wallet node applies to its locally cached credential row.
/// </summary>
/// <remarks>
/// Multi-node audit CRITICAL #2 fix. The W3C BitstringStatusList update remains the
/// authoritative cross-node mechanism for verifier-side status checks; this payload
/// keeps the holder's local cached row in sync without relying on direct
/// wallet-to-wallet HTTP, which silently no-ops on multi-node deployments.
/// </remarks>
public class CredentialStatusChangePayload
{
    /// <summary>
    /// DID URI of the credential whose status is changing.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(500)]
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>
    /// New status — one of <c>Revoked</c>, <c>Suspended</c>, <c>Active</c>.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(32)]
    [JsonPropertyName("newStatus")]
    public string NewStatus { get; set; } = string.Empty;

    /// <summary>
    /// Wallet address of the issuing authority requesting the status change.
    /// Holder-side handler verifies this matches the credential's original issuer
    /// before applying the change.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("issuerWallet")]
    public string IssuerWallet { get; set; } = string.Empty;

    /// <summary>
    /// Wallet address of the credential holder (subject). Drives recipient routing.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("subjectDid")]
    public string SubjectDid { get; set; } = string.Empty;

    /// <summary>
    /// Issuer-supplied reason — surfaced in holder UI and audit logs.
    /// </summary>
    [DataAnnotations.MaxLength(1000)]
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    /// <summary>
    /// Timestamp of the status change.
    /// </summary>
    [DataAnnotations.Required]
    [JsonPropertyName("changedAt")]
    public DateTimeOffset ChangedAt { get; set; }

    /// <summary>
    /// Identifier of the Bitstring Status List this change applies to.
    /// </summary>
    /// <remarks>
    /// The event must be self-describing: another node folding this transaction has no access to the
    /// issuing node's wallet, so it cannot look the credential up to discover which list and bit the
    /// change refers to. Without these two fields a status list can never be rebuilt from the ledger,
    /// which is the whole point of writing the event (#1482).
    /// </remarks>
    public string? StatusListId { get; set; }

    /// <summary>
    /// Index of this credential's bit within <see cref="StatusListId"/>.
    /// </summary>
    /// <remarks>
    /// Nullable because a credential issued before status lists were allocated carries no index;
    /// such an event records the status change for audit but cannot be projected onto a bitstring.
    /// </remarks>
    public int? StatusListIndex { get; set; }
}
