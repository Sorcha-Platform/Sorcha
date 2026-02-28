// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Thread-safe in-memory queue for validated transactions ready for docket building.
/// Uses priority ordering and TTL-based expiry.
/// </summary>
public class VerifiedTransactionQueue : IVerifiedTransactionQueue
{
    private readonly VerifiedQueueConfiguration _config;
    private readonly ILogger<VerifiedTransactionQueue> _logger;

    // Per-register queues
    private readonly ConcurrentDictionary<string, RegisterQueue> _queues = new();

    // Global statistics
    private long _totalEnqueued;
    private long _totalDequeued;
    private long _totalExpired;
    private readonly object _statsLock = new();

    public VerifiedTransactionQueue(
        IOptions<VerifiedQueueConfiguration> config,
        ILogger<VerifiedTransactionQueue> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool Enqueue(string registerId, Transaction transaction, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(transaction.TransactionId);

        // Check global limits
        if (GetTotalCount() >= _config.MaxTotalTransactions)
        {
            _logger.LogWarning(
                "Cannot enqueue transaction {TransactionId} for register {RegisterId}: global limit reached ({Max})",
                transaction.TransactionId, registerId, _config.MaxTotalTransactions);
            return false;
        }

        // Check register limit
        if (_queues.Count >= _config.MaxRegisters && !_queues.ContainsKey(registerId))
        {
            _logger.LogWarning(
                "Cannot enqueue transaction for register {RegisterId}: max registers reached ({Max})",
                registerId, _config.MaxRegisters);
            return false;
        }

        var queue = _queues.GetOrAdd(registerId, _ => new RegisterQueue(_config.MaxTransactionsPerRegister));

        var verifiedTx = new VerifiedTransaction
        {
            Transaction = transaction,
            EnqueuedAt = DateTimeOffset.UtcNow,
            Priority = priority,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_config.TransactionTtl)
        };

        if (!queue.TryEnqueue(verifiedTx))
        {
            _logger.LogWarning(
                "Cannot enqueue transaction {TransactionId} for register {RegisterId}: register limit reached ({Max})",
                transaction.TransactionId, registerId, _config.MaxTransactionsPerRegister);
            return false;
        }

        Interlocked.Increment(ref _totalEnqueued);

        _logger.LogDebug(
            "Enqueued transaction {TransactionId} for register {RegisterId} with priority {Priority}",
            transaction.TransactionId, registerId, priority);

        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyList<VerifiedTransaction> Dequeue(string registerId, int maxCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        if (maxCount <= 0) return [];

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return [];
        }

        var transactions = queue.Dequeue(maxCount);

        if (transactions.Count > 0)
        {
            Interlocked.Add(ref _totalDequeued, transactions.Count);

            _logger.LogDebug(
                "Dequeued {Count} transactions for register {RegisterId}",
                transactions.Count, registerId);
        }

        return transactions;
    }

    /// <inheritdoc/>
    public IReadOnlyList<VerifiedTransaction> Peek(string registerId, int maxCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        if (maxCount <= 0) return [];

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return [];
        }

        return queue.Peek(maxCount);
    }

    /// <inheritdoc/>
    public void ReturnToQueue(string registerId, IReadOnlyList<VerifiedTransaction> transactions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactions);

        if (transactions.Count == 0) return;

        var queue = _queues.GetOrAdd(registerId, _ => new RegisterQueue(_config.MaxTransactionsPerRegister));

        var now = DateTimeOffset.UtcNow;
        var returned = 0;

        foreach (var tx in transactions)
        {
            // Don't return expired transactions
            if (tx.ExpiresAt <= now)
            {
                Interlocked.Increment(ref _totalExpired);
                continue;
            }

            if (queue.TryEnqueue(tx))
            {
                returned++;
            }
        }

        // Adjust dequeue count since we returned them
        Interlocked.Add(ref _totalDequeued, -returned);

        _logger.LogDebug(
            "Returned {Count} transactions to queue for register {RegisterId}",
            returned, registerId);
    }

    /// <inheritdoc/>
    public bool Remove(string registerId, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return false;
        }

        return queue.Remove(transactionId);
    }

    /// <inheritdoc/>
    public bool Contains(string registerId, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return false;
        }

        return queue.Contains(transactionId);
    }

    /// <inheritdoc/>
    public int GetCount(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return 0;
        }

        return queue.Count;
    }

    /// <inheritdoc/>
    public int GetTotalCount()
    {
        return _queues.Values.Sum(q => q.Count);
    }

    /// <inheritdoc/>
    public VerifiedQueueStats GetStats()
    {
        var activeQueues = _queues.Where(kvp => kvp.Value.Count > 0).ToList();
        var totalCount = activeQueues.Sum(kvp => kvp.Value.Count);

        DateTimeOffset? oldest = null;
        DateTimeOffset? newest = null;

        foreach (var queue in activeQueues.Select(kvp => kvp.Value))
        {
            var stats = queue.GetStats();
            if (stats.OldestTransaction.HasValue)
            {
                if (!oldest.HasValue || stats.OldestTransaction.Value < oldest.Value)
                    oldest = stats.OldestTransaction;
            }
            if (stats.NewestTransaction.HasValue)
            {
                if (!newest.HasValue || stats.NewestTransaction.Value > newest.Value)
                    newest = stats.NewestTransaction;
            }
        }

        return new VerifiedQueueStats
        {
            TotalTransactions = totalCount,
            ActiveRegisters = activeQueues.Count,
            AverageTransactionsPerRegister = activeQueues.Count > 0
                ? (double)totalCount / activeQueues.Count
                : 0,
            OldestTransaction = oldest,
            NewestTransaction = newest,
            TotalEnqueued = Interlocked.Read(ref _totalEnqueued),
            TotalDequeued = Interlocked.Read(ref _totalDequeued),
            TotalExpired = Interlocked.Read(ref _totalExpired)
        };
    }

    /// <inheritdoc/>
    public RegisterQueueStats GetRegisterStats(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return new RegisterQueueStats
            {
                RegisterId = registerId,
                TransactionCount = 0
            };
        }

        var stats = queue.GetStats();

        return new RegisterQueueStats
        {
            RegisterId = registerId,
            TransactionCount = stats.Count,
            OldestTransaction = stats.OldestTransaction,
            NewestTransaction = stats.NewestTransaction,
            AveragePriority = stats.AveragePriority
        };
    }

    /// <inheritdoc/>
    public int Clear(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (!_queues.TryRemove(registerId, out var queue))
        {
            return 0;
        }

        var count = queue.Count;

        _logger.LogInformation(
            "Cleared {Count} transactions for register {RegisterId}",
            count, registerId);

        return count;
    }

    /// <inheritdoc/>
    public int ClearAll()
    {
        var total = GetTotalCount();
        _queues.Clear();

        _logger.LogInformation("Cleared all verified transaction queues ({Count} transactions)", total);

        return total;
    }

    /// <inheritdoc/>
    public int CleanupExpired()
    {
        var totalRemoved = 0;

        foreach (var kvp in _queues)
        {
            var removed = kvp.Value.RemoveExpired();
            if (removed > 0)
            {
                totalRemoved += removed;
                Interlocked.Add(ref _totalExpired, removed);

                _logger.LogDebug(
                    "Removed {Count} expired transactions from register {RegisterId}",
                    removed, kvp.Key);
            }
        }

        // Remove empty queues
        var emptyQueues = _queues.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var registerId in emptyQueues)
        {
            _queues.TryRemove(registerId, out _);
        }

        if (totalRemoved > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired transactions across all registers", totalRemoved);
        }

        return totalRemoved;
    }


    /// <summary>
    /// Thread-safe priority queue for a single register
    /// </summary>
    private class RegisterQueue
    {
        private readonly int _maxCapacity;
        private readonly object _lock = new();
        private readonly SortedSet<VerifiedTransaction> _queue;
        private readonly Dictionary<string, VerifiedTransaction> _byId = new();

        public RegisterQueue(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _queue = new SortedSet<VerifiedTransaction>(new PriorityComparer());
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count;
                }
            }
        }

        public bool TryEnqueue(VerifiedTransaction tx)
        {
            lock (_lock)
            {
                // Check capacity
                if (_queue.Count >= _maxCapacity)
                    return false;

                // Check for duplicate
                if (_byId.ContainsKey(tx.TransactionId))
                    return false;

                _queue.Add(tx);
                _byId[tx.TransactionId] = tx;
                return true;
            }
        }

        public IReadOnlyList<VerifiedTransaction> Dequeue(int maxCount)
        {
            var result = new List<VerifiedTransaction>();

            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var toRemove = new List<VerifiedTransaction>();

                foreach (var tx in _queue)
                {
                    if (result.Count >= maxCount)
                        break;

                    // Skip expired
                    if (tx.ExpiresAt <= now)
                    {
                        toRemove.Add(tx);
                        continue;
                    }

                    result.Add(tx);
                    toRemove.Add(tx);
                }

                foreach (var tx in toRemove)
                {
                    _queue.Remove(tx);
                    _byId.Remove(tx.TransactionId);
                }
            }

            return result;
        }

        public IReadOnlyList<VerifiedTransaction> Peek(int maxCount)
        {
            var result = new List<VerifiedTransaction>();

            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                foreach (var tx in _queue)
                {
                    if (result.Count >= maxCount)
                        break;

                    // Skip expired
                    if (tx.ExpiresAt <= now)
                        continue;

                    result.Add(tx);
                }
            }

            return result;
        }

        public bool Remove(string transactionId)
        {
            lock (_lock)
            {
                if (!_byId.TryGetValue(transactionId, out var tx))
                    return false;

                _queue.Remove(tx);
                _byId.Remove(transactionId);
                return true;
            }
        }

        public bool Contains(string transactionId)
        {
            lock (_lock)
            {
                return _byId.ContainsKey(transactionId);
            }
        }

        public int RemoveExpired()
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var expired = _queue.Where(tx => tx.ExpiresAt <= now).ToList();

                foreach (var tx in expired)
                {
                    _queue.Remove(tx);
                    _byId.Remove(tx.TransactionId);
                }

                return expired.Count;
            }
        }

        public (int Count, DateTimeOffset? OldestTransaction, DateTimeOffset? NewestTransaction, double AveragePriority) GetStats()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                    return (0, null, null, 0);

                var now = DateTimeOffset.UtcNow;
                var valid = _queue.Where(tx => tx.ExpiresAt > now).ToList();

                if (valid.Count == 0)
                    return (0, null, null, 0);

                return (
                    valid.Count,
                    valid.Min(tx => tx.EnqueuedAt),
                    valid.Max(tx => tx.EnqueuedAt),
                    valid.Average(tx => tx.Priority)
                );
            }
        }

        /// <summary>
        /// Comparer for priority ordering (higher priority first, then by enqueue time)
        /// </summary>
        private class PriorityComparer : IComparer<VerifiedTransaction>
        {
            public int Compare(VerifiedTransaction? x, VerifiedTransaction? y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return 1;
                if (y == null) return -1;

                // Higher priority first (descending)
                var priorityCompare = y.Priority.CompareTo(x.Priority);
                if (priorityCompare != 0) return priorityCompare;

                // Earlier enqueue time first (ascending - FIFO within same priority)
                var timeCompare = x.EnqueuedAt.CompareTo(y.EnqueuedAt);
                if (timeCompare != 0) return timeCompare;

                // Fallback to transaction ID for uniqueness
                return string.Compare(x.TransactionId, y.TransactionId, StringComparison.Ordinal);
            }
        }
    }

}
