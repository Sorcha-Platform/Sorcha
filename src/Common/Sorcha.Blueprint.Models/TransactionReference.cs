// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text.Json.Serialization;
using Sorcha.Blueprint.Models.JsonLd;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Represents a lightweight reference to a blockchain transaction
/// Implements the JSON-LD Blockchain Transaction Format specification
/// </summary>
/// <remarks>
/// This model provides a compact way to reference transactions without including
/// the full transaction data. It supports universal addressability via DID URIs.
///
/// See: docs/blockchain-transaction-format.md
/// </remarks>
public class TransactionReference : IEquatable<TransactionReference>
{
    /// <summary>
    /// JSON-LD context for semantic web integration
    /// </summary>
    [JsonPropertyName("@context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Context { get; set; } = BlockchainContext.ContextUrl;

    /// <summary>
    /// JSON-LD type designation
    /// </summary>
    [JsonPropertyName("@type")]
    public string Type { get; set; } = "TransactionReference";

    /// <summary>
    /// JSON-LD universal identifier (DID URI)
    /// Format: did:sorcha:register:{registerId}/tx/{txId}
    /// </summary>
    [JsonPropertyName("@id")]
    [DataAnnotations.Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Register (ledger) identifier
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.StringLength(64)]
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Transaction identifier (hash)
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.StringLength(64)]
    [JsonPropertyName("txId")]
    public string TxId { get; set; } = string.Empty;

    /// <summary>
    /// Transaction timestamp (UTC)
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Creates a transaction reference from register ID and transaction ID
    /// </summary>
    /// <param name="registerId">Register identifier</param>
    /// <param name="txId">Transaction identifier</param>
    /// <param name="timestamp">Transaction timestamp (defaults to current UTC time)</param>
    /// <returns>TransactionReference with generated DID URI</returns>
    public static TransactionReference Create(string registerId, string txId, DateTime? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId, nameof(registerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(txId, nameof(txId));

        var didUri = BlockchainContext.GenerateDidUri(registerId, txId);

        return new TransactionReference
        {
            Id = didUri,
            RegisterId = registerId,
            TxId = txId,
            Timestamp = timestamp ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a transaction reference from a DID URI
    /// </summary>
    /// <param name="didUri">DID URI to parse</param>
    /// <param name="timestamp">Transaction timestamp (defaults to current UTC time)</param>
    /// <returns>TransactionReference or null if DID URI is invalid</returns>
    public static TransactionReference? FromDidUri(string didUri, DateTime? timestamp = null)
    {
        var parsed = BlockchainContext.ParseDidUri(didUri);
        if (parsed == null)
            return null;

        return new TransactionReference
        {
            Id = didUri,
            RegisterId = parsed.Value.registerId,
            TxId = parsed.Value.txId,
            Timestamp = timestamp ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Validates that this transaction reference has a valid DID URI format
    /// </summary>
    /// <returns>True if valid</returns>
    public bool IsValid()
    {
        return BlockchainContext.IsValidDidUri(Id) &&
               !string.IsNullOrWhiteSpace(RegisterId) &&
               !string.IsNullOrWhiteSpace(TxId);
    }

    /// <summary>
    /// Ensures the DID URI matches the RegisterId and TxId
    /// </summary>
    public void RegenerateDidUri()
    {
        if (!string.IsNullOrWhiteSpace(RegisterId) && !string.IsNullOrWhiteSpace(TxId))
        {
            Id = BlockchainContext.GenerateDidUri(RegisterId, TxId);
        }
    }

    /// <inheritdoc/>
    public bool Equals(TransactionReference? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Id == other.Id &&
               RegisterId == other.RegisterId &&
               TxId == other.TxId;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return Equals(obj as TransactionReference);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(Id, RegisterId, TxId);
    }

    /// <summary>
    /// Equality operator
    /// </summary>
    public static bool operator ==(TransactionReference? left, TransactionReference? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    /// <summary>
    /// Inequality operator
    /// </summary>
    public static bool operator !=(TransactionReference? left, TransactionReference? right)
    {
        return !(left == right);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Id;
    }
}
