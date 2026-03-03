// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Delivers inbound action notifications to users.
/// Resolves wallet address to user, checks notification preferences,
/// and publishes via Redis pub/sub (real-time) or queues to Redis sorted set (digest).
/// </summary>
public interface INotificationDeliveryService
{
    /// <summary>
    /// Deliver a notification for an inbound action transaction.
    /// Resolves the recipient address to a wallet and user, checks notification preferences,
    /// and routes to the appropriate delivery channel.
    /// </summary>
    /// <param name="recipientAddress">Wallet address that matched the bloom filter.</param>
    /// <param name="transactionId">64-char hex SHA-256 transaction hash.</param>
    /// <param name="registerId">Register the transaction belongs to.</param>
    /// <param name="docketNumber">Docket number containing this transaction.</param>
    /// <param name="blueprintId">Blueprint ID from transaction metadata.</param>
    /// <param name="instanceId">Blueprint instance ID from transaction metadata.</param>
    /// <param name="actionId">Action ID from transaction metadata.</param>
    /// <param name="nextActionId">Next action ID from transaction metadata.</param>
    /// <param name="senderAddress">Transaction sender address.</param>
    /// <param name="timestamp">Transaction timestamp.</param>
    /// <param name="isRecovery">Whether this is a recovery-mode notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The delivery status indicating how the notification was handled.</returns>
    Task<NotificationDeliveryResult> DeliverAsync(
        string recipientAddress,
        string transactionId,
        string registerId,
        long docketNumber,
        string? blueprintId,
        string? instanceId,
        uint actionId,
        uint nextActionId,
        string? senderAddress,
        DateTimeOffset timestamp,
        bool isRecovery,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a notification delivery attempt.</summary>
public enum NotificationDeliveryResult
{
    /// <summary>Delivered in real-time via Redis pub/sub → SignalR.</summary>
    DeliveredRealTime = 1,

    /// <summary>Queued for digest delivery.</summary>
    QueuedForDigest = 2,

    /// <summary>Rate-limited — overflow queued for digest.</summary>
    RateLimited = 3,

    /// <summary>No user found for the recipient address.</summary>
    NoUserFound = 4
}
