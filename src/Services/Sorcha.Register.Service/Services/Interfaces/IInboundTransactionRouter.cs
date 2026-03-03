// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Service.Services.Interfaces;

/// <summary>
/// Routes inbound transactions by checking recipient addresses against the local bloom filter.
/// Only action-type transactions are routed; control, docket, and participant types are skipped.
/// On match, notifies the Wallet Service via gRPC for user notification delivery.
/// </summary>
public interface IInboundTransactionRouter
{
    /// <summary>
    /// Route a single transaction by checking its recipient addresses against the bloom filter.
    /// </summary>
    /// <param name="registerId">Register the transaction belongs to.</param>
    /// <param name="transactionId">64-char hex SHA-256 transaction hash.</param>
    /// <param name="transactionType">Type of transaction (only Action type is routed).</param>
    /// <param name="recipientAddresses">Recipient wallet addresses to check against the bloom filter.</param>
    /// <param name="senderAddress">Sender address (for notification context).</param>
    /// <param name="metadata">Transaction metadata containing blueprint/action IDs.</param>
    /// <param name="docketNumber">Docket number containing this transaction.</param>
    /// <param name="isRecovery">Whether this is a recovery-mode transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of recipient addresses that matched the bloom filter and were notified.</returns>
    Task<int> RouteTransactionAsync(
        string registerId,
        string transactionId,
        TransactionType transactionType,
        IReadOnlyList<string> recipientAddresses,
        string? senderAddress,
        TransactionMetaData? metadata,
        long docketNumber,
        bool isRecovery = false,
        CancellationToken cancellationToken = default);
}
