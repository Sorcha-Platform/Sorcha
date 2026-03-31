// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Manages transaction lifecycle tracking for wallets.
/// Updates WalletTransaction records as transactions progress through:
/// Pending → Submitted → Confirmed (sealed/blue tick) → Receipted (double blue tick).
/// </summary>
public class TransactionLifecycleService : ITransactionLifecycleService
{
    private readonly WalletDbContext _db;
    private readonly ILogger<TransactionLifecycleService> _logger;

    public TransactionLifecycleService(WalletDbContext db, ILogger<TransactionLifecycleService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task RecordSubmissionAsync(
        string walletAddress, string transactionId, string registerId,
        string? counterpartyAddress = null, CancellationToken ct = default)
    {
        // Check if already tracked (idempotent)
        var existing = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);

        if (existing != null)
        {
            _logger.LogDebug("Transaction {TxId} already tracked, skipping", transactionId);
            return;
        }

        var record = new WalletTransaction
        {
            TransactionId = transactionId,
            ParentWalletAddress = walletAddress,
            TransactionType = "sent",
            RegisterId = registerId,
            Direction = "Outbound",
            CounterpartyAddress = counterpartyAddress,
            State = TransactionState.Submitted,
            CreatedAt = DateTime.UtcNow
        };

        _db.WalletTransactions.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Recorded outbound transaction {TxId} for wallet {Address}",
            transactionId, walletAddress);
    }

    /// <inheritdoc />
    public async Task RecordInboundAsync(
        string walletAddress, string transactionId, string registerId,
        string? counterpartyAddress = null, CancellationToken ct = default)
    {
        var existing = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId
                && t.ParentWalletAddress == walletAddress, ct);

        if (existing != null)
            return;

        var record = new WalletTransaction
        {
            TransactionId = transactionId,
            ParentWalletAddress = walletAddress,
            TransactionType = "received",
            RegisterId = registerId,
            Direction = "Inbound",
            CounterpartyAddress = counterpartyAddress,
            State = TransactionState.Submitted,
            CreatedAt = DateTime.UtcNow
        };

        _db.WalletTransactions.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Recorded inbound transaction {TxId} for wallet {Address}",
            transactionId, walletAddress);
    }

    /// <inheritdoc />
    public async Task MarkSealedAsync(string transactionId, long docketNumber, CancellationToken ct = default)
    {
        var records = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TransactionId == transactionId)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            _logger.LogDebug("Transaction {TxId} not tracked, skipping seal update", transactionId);
            return;
        }

        foreach (var record in records
            .Where(r => r.State is not (TransactionState.Confirmed or TransactionState.Receipted)))
        {
            record.State = TransactionState.Confirmed;
            record.ConfirmedAt = DateTime.UtcNow;
            record.BlockHeight = docketNumber;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Marked transaction {TxId} as sealed in docket {Docket}",
            transactionId, docketNumber);
    }

    /// <inheritdoc />
    public async Task MarkReceiptedAsync(string transactionId, string receiptId, CancellationToken ct = default)
    {
        var records = await _db.WalletTransactions
            .IgnoreQueryFilters()
            .Where(t => t.TransactionId == transactionId)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            _logger.LogDebug("Transaction {TxId} not tracked, skipping receipt update", transactionId);
            return;
        }

        foreach (var record in records
            .Where(r => r.State != TransactionState.Receipted))
        {
            record.State = TransactionState.Receipted;
            record.ReceiptId = receiptId;
            record.ReceiptedAt = DateTime.UtcNow;

            // If somehow skipped Confirmed, backfill
            record.ConfirmedAt ??= DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Marked transaction {TxId} as receipted with receipt {ReceiptId}",
            transactionId, receiptId);
    }

    /// <inheritdoc />
    public async Task<WalletTransaction?> GetStatusAsync(
        string walletAddress, string transactionId, CancellationToken ct = default)
    {
        return await _db.WalletTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId
                && t.ParentWalletAddress == walletAddress, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WalletTransaction>> GetRecentTransactionsAsync(
        string walletAddress, int limit = 50, CancellationToken ct = default)
    {
        return await _db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.ParentWalletAddress == walletAddress)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
