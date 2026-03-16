// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Represents a transaction associated with a wallet
/// </summary>
public class WalletTransaction
{
    /// <summary>
    /// Transaction hash/ID
    /// </summary>
    public required string TransactionId { get; set; }

    /// <summary>
    /// Parent wallet address
    /// </summary>
    public required string ParentWalletAddress { get; set; }

    /// <summary>
    /// Transaction type (sent, received, self)
    /// </summary>
    public required string TransactionType { get; set; }

    /// <summary>
    /// Amount (if applicable)
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Transaction state
    /// </summary>
    public TransactionState State { get; set; } = TransactionState.Pending;

    /// <summary>
    /// Timestamp when transaction was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when transaction was confirmed
    /// </summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Block height (if confirmed)
    /// </summary>
    public long? BlockHeight { get; set; }

    /// <summary>
    /// Raw transaction data (optional)
    /// </summary>
    public string? RawTransaction { get; set; }

    /// <summary>
    /// Custom metadata
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Navigation property to parent wallet
    /// </summary>
    public Wallet? Wallet { get; set; }
}
