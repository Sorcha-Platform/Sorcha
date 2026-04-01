// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Manages transaction lifecycle tracking for wallets.
/// Records submitted transactions and updates their status as they progress
/// through the lifecycle: Pending → Submitted → Confirmed (sealed) → Receipted.
/// </summary>
public interface ITransactionLifecycleService
{
    /// <summary>
    /// Records a new outbound transaction submission (grey tick).
    /// </summary>
    Task RecordSubmissionAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        string? counterpartyAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records a new inbound transaction (grey tick for incoming).
    /// </summary>
    Task RecordInboundAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        string? counterpartyAddress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a transaction as sealed in a docket (blue tick).
    /// Called when docket:confirmed event is received.
    /// </summary>
    Task MarkSealedAsync(
        string transactionId,
        long docketNumber,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a transaction as receipted — cryptographic proof of finality (double blue tick).
    /// Called when receipt:generated event is received.
    /// </summary>
    Task MarkReceiptedAsync(
        string transactionId,
        string receiptId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the lifecycle status of a specific transaction.
    /// </summary>
    Task<WalletTransaction?> GetStatusAsync(
        string walletAddress,
        string transactionId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets recent transactions for a wallet with their lifecycle state.
    /// </summary>
    Task<IReadOnlyList<WalletTransaction>> GetRecentTransactionsAsync(
        string walletAddress,
        int limit = 50,
        CancellationToken ct = default);
}
