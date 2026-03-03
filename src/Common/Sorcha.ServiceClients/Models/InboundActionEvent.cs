// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Models;

/// <summary>
/// Represents a detected inbound action transaction destined for a local wallet address.
/// Published to Redis pub/sub (wallet:notifications) for real-time delivery,
/// or queued to Redis sorted set (wallet:digest:{userId}) for digest batching.
/// </summary>
public record InboundActionEvent
{
    /// <summary>Unique event identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Recipient wallet address that matched the bloom filter.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>Wallet ID containing the matched address.</summary>
    public Guid WalletId { get; init; }

    /// <summary>Owner's user sub (from Wallet.Owner).</summary>
    public required string UserId { get; init; }

    /// <summary>Tenant identifier.</summary>
    public string? TenantId { get; init; }

    /// <summary>Blueprint ID from TransactionMetaData.</summary>
    public string? BlueprintId { get; init; }

    /// <summary>Blueprint instance ID from TransactionMetaData.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Action ID from TransactionMetaData.</summary>
    public uint ActionId { get; init; }

    /// <summary>Next action ID from TransactionMetaData.</summary>
    public uint NextActionId { get; init; }

    /// <summary>Transaction sender address (if available).</summary>
    public string? SenderAddress { get; init; }

    /// <summary>64-char hex SHA-256 hash (TxId).</summary>
    public required string TransactionId { get; init; }

    /// <summary>Register the transaction belongs to.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Docket number containing this transaction.</summary>
    public long DocketNumber { get; init; }

    /// <summary>When the event was detected.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True if detected during recovery mode.</summary>
    public bool IsRecoveryEvent { get; init; }
}
