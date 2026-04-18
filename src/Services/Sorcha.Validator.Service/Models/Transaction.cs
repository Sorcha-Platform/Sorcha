// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// A signed record of a blueprint action execution awaiting validation and inclusion in a docket
/// </summary>
public class Transaction
{
    // Identity
    /// <summary>
    /// Unique transaction identifier (GUID or hash)
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// Target register ID
    /// </summary>
    public required string RegisterId { get; init; }

    // Blueprint Context
    /// <summary>
    /// Blueprint definition ID (null for non-blueprint transactions such as Participant)
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// Specific action within the blueprint (null for non-blueprint transactions)
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// Action-specific data payload
    /// </summary>
    public required JsonElement Payload { get; init; }

    /// <summary>
    /// JSON-serialized payload (for gRPC transmission).
    /// Excluded from JSON serialization to prevent interference with Payload during
    /// Redis round-trips (PayloadJson as a string would produce different hash than Payload as JsonElement).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? PayloadJson => Payload.ValueKind != JsonValueKind.Undefined
        ? Payload.GetRawText()
        : null;

    // Timestamps
    /// <summary>
    /// Transaction creation timestamp
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional TTL for memory pool eviction
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    // Cryptography
    /// <summary>
    /// One or more signatures authorizing this transaction
    /// </summary>
    public required List<Signature> Signatures { get; init; }

    /// <summary>
    /// SHA256 hash of the payload
    /// </summary>
    public required string PayloadHash { get; init; }

    // Memory Pool Management
    /// <summary>
    /// Priority level for memory pool ordering
    /// </summary>
    public TransactionPriority Priority { get; set; } = TransactionPriority.Normal;

    /// <summary>
    /// When this transaction was added to the memory pool
    /// </summary>
    public DateTimeOffset AddedToPoolAt { get; set; }

    /// <summary>
    /// Number of times this transaction failed consensus and was retried
    /// </summary>
    public int RetryCount { get; set; }

    // Replay Protection
    /// <summary>
    /// Per-sender monotonic sequence number for replay protection (SEC-AUDIT 4.2).
    /// Must equal sender's last sequence number + 1 on the target register.
    /// Genesis/control transactions use 0 and bypass validation.
    /// </summary>
    public ulong SequenceNumber { get; init; }

    // Chain Linkage
    /// <summary>
    /// Previous transaction ID for chain linkage. Null for genesis/independent transactions.
    /// Empty string is treated as null (no previous transaction).
    /// </summary>
    public string? PreviousTransactionId { get; init; }

    // Recipients
    /// <summary>
    /// Wallet addresses of transaction recipients. Extracted from disclosure group
    /// wrappedKeys at transaction build time. Used by the Register Service to route
    /// docket-sealed transactions to recipient Wallet Services for inbound detection.
    /// </summary>
    public List<string>? RecipientsWallets { get; init; }

    // Metadata
    /// <summary>
    /// Extensible key-value metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}
