// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// In-process <see cref="IVerifiedTransactionQueue"/> implementation backed
/// by per-register priority sets with lease-tracked claims. Used as the
/// dev/test fallback; PR 8 ships a Redis-backed implementation that
/// survives validator process restart.
/// </summary>
/// <remarks>
/// On the audited storage list — Production / Staging refuse to start when
/// this implementation is selected (unless overridden by
/// <c>Storage:AllowInMemoryInProduction=true</c>).
/// </remarks>
public class InMemoryVerifiedTransactionQueue : IVerifiedTransactionQueue
{
    private readonly VerifiedQueueConfiguration _config;
    private readonly ILogger<InMemoryVerifiedTransactionQueue> _logger;

    // Per-register state.
    private readonly ConcurrentDictionary<string, RegisterQueue> _queues = new();

    // Global statistics.
    private long _totalEnqueued;
    private long _totalConfirmed;
    private long _totalExpired;

    public InMemoryVerifiedTransactionQueue(
        IOptions<VerifiedQueueConfiguration> config,
        ILogger<InMemoryVerifiedTransactionQueue> logger)
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

        if (GetTotalCount() >= _config.MaxTotalTransactions)
        {
            _logger.LogWarning(
                "Cannot enqueue transaction {TransactionId} for register {RegisterId}: global limit reached ({Max})",
                transaction.TransactionId, registerId, _config.MaxTotalTransactions);
            return false;
        }

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
    public Task<IReadOnlyList<VerifiedTransactionLease>> ClaimAsync(
        string registerId,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        if (maxCount <= 0)
        {
            return Task.FromResult<IReadOnlyList<VerifiedTransactionLease>>([]);
        }
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        }
        ct.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<VerifiedTransactionLease>>([]);
        }

        var leases = queue.Claim(registerId, maxCount, leaseDuration);
        if (leases.Count > 0)
        {
            _logger.LogDebug(
                "Claimed {Count} transactions for register {RegisterId} (lease {LeaseSeconds}s)",
                leases.Count, registerId, (int)leaseDuration.TotalSeconds);
        }
        return Task.FromResult<IReadOnlyList<VerifiedTransactionLease>>(leases);
    }

    /// <inheritdoc/>
    public Task ConfirmAsync(string registerId, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactionIds);
        ct.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return Task.CompletedTask;
        }

        var confirmed = queue.Confirm(transactionIds);
        if (confirmed > 0)
        {
            Interlocked.Add(ref _totalConfirmed, confirmed);
            _logger.LogDebug(
                "Confirmed {Count} transactions for register {RegisterId}",
                confirmed, registerId);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReleaseAsync(string registerId, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactionIds);
        ct.ThrowIfCancellationRequested();

        if (!_queues.TryGetValue(registerId, out var queue))
        {
            return Task.CompletedTask;
        }

        var (released, expired) = queue.Release(transactionIds);
        if (expired > 0)
        {
            // Caller asked to release a claim, but the transaction's TTL had elapsed
            // while it was held. Count this against TotalExpired so the metric covers
            // both passive (CleanupExpired) and active (Release-time) expiry paths.
            Interlocked.Add(ref _totalExpired, expired);
        }
        if (released > 0 || expired > 0)
        {
            _logger.LogDebug(
                "Released {Count} transactions back to the available pool for register {RegisterId} ({Expired} TTL-expired and dropped)",
                released, registerId, expired);
        }
        return Task.CompletedTask;
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
        return _queues.TryGetValue(registerId, out var queue) ? queue.Count : 0;
    }

    /// <inheritdoc/>
    public int GetTotalCount() => _queues.Values.Sum(q => q.Count);

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
            TotalConfirmed = Interlocked.Read(ref _totalConfirmed),
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
    /// Per-register thread-safe priority queue with lease tracking.
    /// </summary>
    private class RegisterQueue
    {
        private readonly int _maxCapacity;
        private readonly object _lock = new();
        // Available pool — sorted by priority (desc) then enqueue time (asc).
        private readonly SortedSet<VerifiedTransaction> _available;
        // Claimed transactions, keyed by tx id, with their lease expiry.
        private readonly Dictionary<string, ClaimedEntry> _claimed = new();
        // Index over both sets so Contains / Remove work uniformly.
        private readonly Dictionary<string, VerifiedTransaction> _byId = new();

        public RegisterQueue(int maxCapacity)
        {
            _maxCapacity = maxCapacity;
            _available = new SortedSet<VerifiedTransaction>(new PriorityComparer());
        }

        public int Count
        {
            get { lock (_lock) { return _available.Count + _claimed.Count; } }
        }

        public bool TryEnqueue(VerifiedTransaction tx)
        {
            lock (_lock)
            {
                if (_available.Count + _claimed.Count >= _maxCapacity)
                    return false;
                if (_byId.ContainsKey(tx.TransactionId))
                    return false;
                _available.Add(tx);
                _byId[tx.TransactionId] = tx;
                return true;
            }
        }

        public IReadOnlyList<VerifiedTransactionLease> Claim(string registerId, int maxCount, TimeSpan leaseDuration)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                // First, auto-release any leases that have expired since the last claim.
                var expiredClaims = _claimed
                    .Where(kvp => kvp.Value.LeaseExpiresAt <= now)
                    .ToList();
                foreach (var (txId, claimed) in expiredClaims)
                {
                    _claimed.Remove(txId);
                    if (_byId.TryGetValue(txId, out var tx))
                    {
                        _available.Add(tx);
                    }
                }

                // Skip TTL-expired transactions silently — they'll be removed by CleanupExpired.
                var leaseExpiresAt = now.Add(leaseDuration);
                var leases = new List<VerifiedTransactionLease>(maxCount);
                var taken = new List<VerifiedTransaction>();
                foreach (var tx in _available)
                {
                    if (leases.Count >= maxCount) break;
                    if (tx.ExpiresAt <= now) continue;
                    leases.Add(new VerifiedTransactionLease
                    {
                        RegisterId = registerId,
                        Transaction = tx,
                        LeaseExpiresAt = leaseExpiresAt
                    });
                    taken.Add(tx);
                }

                foreach (var tx in taken)
                {
                    _available.Remove(tx);
                    _claimed[tx.TransactionId] = new ClaimedEntry(tx, leaseExpiresAt);
                }

                return leases;
            }
        }

        public int Confirm(IEnumerable<string> transactionIds)
        {
            lock (_lock)
            {
                var confirmed = 0;
                foreach (var txId in transactionIds)
                {
                    if (_claimed.Remove(txId))
                    {
                        _byId.Remove(txId);
                        confirmed++;
                    }
                }
                return confirmed;
            }
        }

        public (int Released, int Expired) Release(IEnumerable<string> transactionIds)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var released = 0;
                var expired = 0;
                foreach (var txId in transactionIds)
                {
                    if (_claimed.Remove(txId, out var entry))
                    {
                        if (entry.Transaction.ExpiresAt <= now)
                        {
                            _byId.Remove(txId);
                            expired++;
                            continue;
                        }
                        _available.Add(entry.Transaction);
                        released++;
                    }
                }
                return (released, expired);
            }
        }

        public IReadOnlyList<VerifiedTransaction> Peek(int maxCount)
        {
            var result = new List<VerifiedTransaction>(maxCount);
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var tx in _available)
                {
                    if (result.Count >= maxCount) break;
                    if (tx.ExpiresAt <= now) continue;
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
                _available.Remove(tx);
                _claimed.Remove(transactionId);
                _byId.Remove(transactionId);
                return true;
            }
        }

        public bool Contains(string transactionId)
        {
            lock (_lock) { return _byId.ContainsKey(transactionId); }
        }

        public int RemoveExpired()
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                var expiredAvailable = _available.Where(tx => tx.ExpiresAt <= now).ToList();
                foreach (var tx in expiredAvailable)
                {
                    _available.Remove(tx);
                    _byId.Remove(tx.TransactionId);
                }
                var expiredClaimed = _claimed
                    .Where(kvp => kvp.Value.Transaction.ExpiresAt <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var txId in expiredClaimed)
                {
                    _claimed.Remove(txId);
                    _byId.Remove(txId);
                }
                return expiredAvailable.Count + expiredClaimed.Count;
            }
        }

        public (int Count, DateTimeOffset? OldestTransaction, DateTimeOffset? NewestTransaction, double AveragePriority) GetStats()
        {
            lock (_lock)
            {
                var entries = _available.Concat(_claimed.Values.Select(c => c.Transaction)).ToList();
                if (entries.Count == 0) return (0, null, null, 0);

                var now = DateTimeOffset.UtcNow;
                var valid = entries.Where(tx => tx.ExpiresAt > now).ToList();
                if (valid.Count == 0) return (0, null, null, 0);

                return (
                    valid.Count,
                    valid.Min(tx => tx.EnqueuedAt),
                    valid.Max(tx => tx.EnqueuedAt),
                    valid.Average(tx => tx.Priority));
            }
        }

        private sealed record ClaimedEntry(VerifiedTransaction Transaction, DateTimeOffset LeaseExpiresAt);

        /// <summary>
        /// Comparer for priority ordering (higher priority first, then by enqueue time).
        /// </summary>
        private class PriorityComparer : IComparer<VerifiedTransaction>
        {
            public int Compare(VerifiedTransaction? x, VerifiedTransaction? y)
            {
                if (x == null && y == null) return 0;
                if (x == null) return 1;
                if (y == null) return -1;

                var priorityCompare = y.Priority.CompareTo(x.Priority);
                if (priorityCompare != 0) return priorityCompare;

                var timeCompare = x.EnqueuedAt.CompareTo(y.EnqueuedAt);
                if (timeCompare != 0) return timeCompare;

                return string.Compare(x.TransactionId, y.TransactionId, StringComparison.Ordinal);
            }
        }
    }
}
