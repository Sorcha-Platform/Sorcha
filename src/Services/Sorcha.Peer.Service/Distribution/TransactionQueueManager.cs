// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Distribution;

/// <summary>
/// Manages queuing of transactions for offline mode and retry logic.
/// Persists queued transactions to PostgreSQL via PeerDbContext.
/// </summary>
public class TransactionQueueManager : IDisposable
{
    private readonly ILogger<TransactionQueueManager> _logger;
    private readonly OfflineModeConfiguration _configuration;
    private readonly ConcurrentQueue<QueuedTransaction> _queue;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private bool _disposed;

    public TransactionQueueManager(
        ILogger<TransactionQueueManager> logger,
        IOptions<PeerServiceConfiguration> configuration,
        IServiceScopeFactory? scopeFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value?.OfflineMode ?? throw new ArgumentNullException(nameof(configuration));
        _queue = new ConcurrentQueue<QueuedTransaction>();
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Enqueues a transaction for distribution
    /// </summary>
    public async Task<bool> EnqueueAsync(TransactionNotification transaction, CancellationToken cancellationToken = default)
    {
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        // Check queue size limit
        if (_queue.Count >= _configuration.MaxQueueSize)
        {
            _logger.LogWarning("Transaction queue is full ({Count}/{Max}), rejecting transaction {TxId}",
                _queue.Count, _configuration.MaxQueueSize, transaction.TransactionId);
            return false;
        }

        var queuedTx = new QueuedTransaction
        {
            Id = Guid.NewGuid().ToString(),
            Transaction = transaction,
            EnqueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
            Status = QueueStatus.Pending
        };

        _queue.Enqueue(queuedTx);
        _logger.LogInformation("Enqueued transaction {TxId} (queue size: {Size})",
            transaction.TransactionId, _queue.Count);

        // Persist if enabled
        if (_configuration.QueuePersistence)
        {
            await PersistTransactionAsync(queuedTx, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Dequeues the next transaction for processing
    /// </summary>
    public bool TryDequeue(out QueuedTransaction? transaction)
    {
        return _queue.TryDequeue(out transaction);
    }

    /// <summary>
    /// Peeks at the next transaction without removing it
    /// </summary>
    public bool TryPeek(out QueuedTransaction? transaction)
    {
        return _queue.TryPeek(out transaction);
    }

    /// <summary>
    /// Marks a transaction as successfully processed
    /// </summary>
    public async Task MarkAsProcessedAsync(string id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Marked transaction {Id} as processed", id);

        if (_configuration.QueuePersistence)
        {
            await DeleteTransactionAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Marks a transaction as failed and requeues if retries remain
    /// </summary>
    public async Task<bool> MarkAsFailedAsync(QueuedTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (transaction == null)
            return false;

        transaction.RetryCount++;
        transaction.LastAttemptAt = DateTimeOffset.UtcNow;

        if (transaction.RetryCount >= _configuration.MaxRetries)
        {
            _logger.LogWarning("Transaction {TxId} exceeded max retries ({Count}), dropping",
                transaction.Transaction.TransactionId, transaction.RetryCount);
            transaction.Status = QueueStatus.Failed;

            if (_configuration.QueuePersistence)
            {
                await DeleteTransactionAsync(transaction.Id, cancellationToken);
            }

            return false;
        }

        _logger.LogInformation("Transaction {TxId} failed, retry {Retry}/{Max}",
            transaction.Transaction.TransactionId, transaction.RetryCount, _configuration.MaxRetries);

        transaction.Status = QueueStatus.Pending;
        _queue.Enqueue(transaction);

        if (_configuration.QueuePersistence)
        {
            await PersistTransactionAsync(transaction, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Gets the current queue size
    /// </summary>
    public int GetQueueSize() => _queue.Count;

    /// <summary>
    /// Checks if the queue is empty
    /// </summary>
    public bool IsEmpty() => _queue.IsEmpty;

    /// <summary>
    /// Loads transactions from database on startup
    /// </summary>
    public async Task LoadFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (!_configuration.QueuePersistence || _scopeFactory == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();

            var entities = await context.QueuedTransactions
                .Where(e => e.Status == QueueStatus.Pending.ToString())
                .OrderBy(e => e.EnqueuedAt)
                .ToListAsync(cancellationToken);

            var loadedCount = 0;
            foreach (var entity in entities)
            {
                _queue.Enqueue(entity.ToDomain());
                loadedCount++;
            }

            _logger.LogInformation("Loaded {Count} transactions from queue database", loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transactions from database");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Persists a transaction to the database
    /// </summary>
    private async Task PersistTransactionAsync(QueuedTransaction queuedTx, CancellationToken cancellationToken)
    {
        if (_scopeFactory == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();

            var entity = QueuedTransactionEntity.FromDomain(queuedTx);
            var existing = await context.QueuedTransactions.FindAsync([entity.Id], cancellationToken);

            if (existing != null)
            {
                existing.RetryCount = entity.RetryCount;
                existing.LastAttemptAt = entity.LastAttemptAt;
                existing.Status = entity.Status;
            }
            else
            {
                context.QueuedTransactions.Add(entity);
            }

            await context.SaveChangesAsync(cancellationToken);

            // Enforce max queue size: trim oldest entries if over limit
            var totalCount = await context.QueuedTransactions.CountAsync(cancellationToken);
            if (totalCount > _configuration.MaxQueueSize)
            {
                var excess = totalCount - _configuration.MaxQueueSize;
                var oldest = await context.QueuedTransactions
                    .OrderBy(e => e.EnqueuedAt)
                    .Take(excess)
                    .ToListAsync(cancellationToken);

                context.QueuedTransactions.RemoveRange(oldest);
                await context.SaveChangesAsync(cancellationToken);

                _logger.LogWarning("Trimmed {Count} oldest transactions from queue database to enforce max size", excess);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting transaction {TxId}", queuedTx.Transaction.TransactionId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Deletes a transaction from the database
    /// </summary>
    private async Task DeleteTransactionAsync(string id, CancellationToken cancellationToken)
    {
        if (_scopeFactory == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();

            if (Guid.TryParse(id, out var guid))
            {
                var entity = await context.QueuedTransactions.FindAsync([guid], cancellationToken);
                if (entity != null)
                {
                    context.QueuedTransactions.Remove(entity);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting transaction {Id}", id);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _dbLock.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// A queued transaction with retry information
/// </summary>
public class QueuedTransaction
{
    public string Id { get; set; } = string.Empty;
    public TransactionNotification Transaction { get; set; } = null!;
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public QueueStatus Status { get; set; }
}

/// <summary>
/// Queue status for transactions
/// </summary>
public enum QueueStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
