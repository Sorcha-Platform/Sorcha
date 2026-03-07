// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Distribution;

namespace Sorcha.Peer.Service.Data;

/// <summary>
/// Database entity for a queued transaction pending distribution.
/// Replaces the previous SQLite-based persistence.
/// </summary>
public class QueuedTransactionEntity
{
    /// <summary>
    /// Unique identifier for this queued entry
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Transaction identifier (from TransactionNotification)
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Register this transaction belongs to
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// ID of the peer that originated this transaction
    /// </summary>
    public string OriginPeerId { get; set; } = string.Empty;

    /// <summary>
    /// When the transaction was created
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Size of the transaction data in bytes
    /// </summary>
    public int DataSize { get; set; }

    /// <summary>
    /// Hash of the transaction data for integrity verification
    /// </summary>
    public string DataHash { get; set; } = string.Empty;

    /// <summary>
    /// Current gossip round number
    /// </summary>
    public int GossipRound { get; set; }

    /// <summary>
    /// Number of hops this notification has traveled
    /// </summary>
    public int HopCount { get; set; }

    /// <summary>
    /// Time to live for this notification (in seconds)
    /// </summary>
    public int TTL { get; set; } = 3600;

    /// <summary>
    /// Whether this notification includes the full transaction data
    /// </summary>
    public bool HasFullData { get; set; }

    /// <summary>
    /// The actual transaction data (may be null if not included)
    /// </summary>
    public byte[]? TransactionData { get; set; }

    /// <summary>
    /// When the transaction was enqueued
    /// </summary>
    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the last attempt was made
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// Current queue status
    /// </summary>
    public string Status { get; set; } = QueueStatus.Pending.ToString();

    /// <summary>
    /// Converts this entity to a domain QueuedTransaction
    /// </summary>
    public QueuedTransaction ToDomain()
    {
        return new QueuedTransaction
        {
            Id = Id.ToString(),
            Transaction = new TransactionNotification
            {
                TransactionId = TransactionId,
                RegisterId = RegisterId,
                OriginPeerId = OriginPeerId,
                Timestamp = Timestamp,
                DataSize = DataSize,
                DataHash = DataHash,
                GossipRound = GossipRound,
                HopCount = HopCount,
                TTL = TTL,
                HasFullData = HasFullData,
                TransactionData = TransactionData
            },
            EnqueuedAt = EnqueuedAt,
            RetryCount = RetryCount,
            LastAttemptAt = LastAttemptAt,
            Status = Enum.Parse<QueueStatus>(Status)
        };
    }

    /// <summary>
    /// Creates an entity from a domain QueuedTransaction
    /// </summary>
    public static QueuedTransactionEntity FromDomain(QueuedTransaction queued)
    {
        var tx = queued.Transaction;
        return new QueuedTransactionEntity
        {
            Id = Guid.TryParse(queued.Id, out var id) ? id : Guid.NewGuid(),
            TransactionId = tx.TransactionId,
            RegisterId = tx.RegisterId,
            OriginPeerId = tx.OriginPeerId,
            Timestamp = tx.Timestamp,
            DataSize = tx.DataSize,
            DataHash = tx.DataHash,
            GossipRound = tx.GossipRound,
            HopCount = tx.HopCount,
            TTL = tx.TTL,
            HasFullData = tx.HasFullData,
            TransactionData = tx.TransactionData,
            EnqueuedAt = queued.EnqueuedAt,
            RetryCount = queued.RetryCount,
            LastAttemptAt = queued.LastAttemptAt,
            Status = queued.Status.ToString()
        };
    }
}
