// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Per-register cache that stores local replicas of register data.
/// Generalizes SystemRegisterCache to work with any register.
/// </summary>
public class RegisterCache
{
    private readonly ILogger<RegisterCache> _logger;
    private readonly ConcurrentDictionary<string, RegisterCacheEntry> _caches = new();
    private readonly int _maxTransactionsPerRegister;
    private readonly int _maxDocketsPerRegister;

    public RegisterCache(
        ILogger<RegisterCache> logger,
        IOptions<PeerServiceConfiguration>? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var syncConfig = configuration?.Value?.RegisterSync ?? new RegisterSyncConfiguration();
        _maxTransactionsPerRegister = syncConfig.MaxCachedTransactionsPerRegister;
        _maxDocketsPerRegister = syncConfig.MaxCachedDocketsPerRegister;
    }

    /// <summary>
    /// Gets or creates the cache entry for a register.
    /// </summary>
    public RegisterCacheEntry GetOrCreate(string registerId)
    {
        return _caches.GetOrAdd(registerId, id =>
            new RegisterCacheEntry(id, _maxTransactionsPerRegister, _maxDocketsPerRegister));
    }

    /// <summary>
    /// Gets the cache entry for a register, or null if not cached.
    /// </summary>
    public RegisterCacheEntry? Get(string registerId)
    {
        _caches.TryGetValue(registerId, out var entry);
        return entry;
    }

    /// <summary>
    /// Removes the cache for a register.
    /// </summary>
    public bool Remove(string registerId)
    {
        return _caches.TryRemove(registerId, out _);
    }

    /// <summary>
    /// Gets all cached register IDs.
    /// </summary>
    public IReadOnlyCollection<string> GetCachedRegisterIds()
    {
        return _caches.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets statistics for all cached registers.
    /// </summary>
    public IReadOnlyDictionary<string, RegisterCacheStatistics> GetAllStatistics()
    {
        return _caches.ToDictionary(
            c => c.Key,
            c => c.Value.GetStatistics());
    }
}

/// <summary>
/// Cache entry for a single register's data.
/// </summary>
public class RegisterCacheEntry
{
    private readonly ConcurrentDictionary<string, CachedTransaction> _transactions = new();
    private readonly ConcurrentDictionary<long, CachedDocket> _dockets = new();
    private readonly object _lock = new();
    private readonly int _maxTransactions;
    private readonly int _maxDockets;
    private long _latestTransactionVersion;
    private long _latestDocketVersion;
    private DateTime _lastUpdateTime = DateTime.MinValue;

    public string RegisterId { get; }

    public RegisterCacheEntry(string registerId, int maxTransactions = 100_000, int maxDockets = 10_000)
    {
        RegisterId = registerId;
        _maxTransactions = maxTransactions;
        _maxDockets = maxDockets;
    }

    /// <summary>
    /// Adds or updates a transaction in the cache.
    /// </summary>
    public void AddOrUpdateTransaction(CachedTransaction tx)
    {
        _transactions[tx.TransactionId] = tx;
        lock (_lock)
        {
            if (tx.Version > _latestTransactionVersion)
                _latestTransactionVersion = tx.Version;
            _lastUpdateTime = DateTime.UtcNow;
        }

        EvictOldTransactionsIfNeeded();
    }

    /// <summary>
    /// Adds or updates a docket in the cache.
    /// </summary>
    public void AddOrUpdateDocket(CachedDocket docket)
    {
        _dockets[docket.Version] = docket;
        lock (_lock)
        {
            if (docket.Version > _latestDocketVersion)
                _latestDocketVersion = docket.Version;
            _lastUpdateTime = DateTime.UtcNow;
        }

        EvictOldDocketsIfNeeded();
    }

    private void EvictOldTransactionsIfNeeded()
    {
        if (_transactions.Count <= _maxTransactions) return;

        var toRemove = _transactions
            .OrderBy(t => t.Value.Version)
            .Take(_transactions.Count - _maxTransactions)
            .Select(t => t.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _transactions.TryRemove(key, out _);
        }
    }

    private void EvictOldDocketsIfNeeded()
    {
        if (_dockets.Count <= _maxDockets) return;

        var toRemove = _dockets
            .OrderBy(d => d.Key)
            .Take(_dockets.Count - _maxDockets)
            .Select(d => d.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _dockets.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Gets a transaction by ID.
    /// </summary>
    public CachedTransaction? GetTransaction(string transactionId)
    {
        _transactions.TryGetValue(transactionId, out var tx);
        return tx;
    }

    /// <summary>
    /// Gets a docket by version.
    /// </summary>
    public CachedDocket? GetDocket(long version)
    {
        _dockets.TryGetValue(version, out var docket);
        return docket;
    }

    /// <summary>
    /// Gets the latest transaction version in the cache.
    /// </summary>
    public long GetLatestTransactionVersion()
    {
        lock (_lock)
        {
            return _latestTransactionVersion;
        }
    }

    /// <summary>
    /// Gets the latest docket version in the cache.
    /// </summary>
    public long GetLatestDocketVersion()
    {
        lock (_lock)
        {
            return _latestDocketVersion;
        }
    }

    public RegisterCacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new RegisterCacheStatistics
            {
                RegisterId = RegisterId,
                TransactionCount = _transactions.Count,
                DocketCount = _dockets.Count,
                LatestTransactionVersion = _latestTransactionVersion,
                LatestDocketVersion = _latestDocketVersion,
                LastUpdateTime = _lastUpdateTime
            };
        }
    }

    /// <summary>
    /// Gets dockets ordered by version, starting from the specified version (exclusive).
    /// </summary>
    public IReadOnlyList<CachedDocket> GetDocketsFromVersion(long fromVersion, int maxDockets = 0)
    {
        var query = _dockets
            .Where(d => d.Key > fromVersion)
            .OrderBy(d => d.Key)
            .Select(d => d.Value);

        if (maxDockets > 0)
            query = query.Take(maxDockets);

        return query.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets transactions with version greater than the specified version, ordered by version.
    /// </summary>
    public IReadOnlyList<CachedTransaction> GetTransactionsFromVersion(long fromVersion)
    {
        return _transactions.Values
            .Where(t => t.Version > fromVersion)
            .OrderBy(t => t.Version)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Clears all cached data for this register.
    /// </summary>
    public void Clear()
    {
        _transactions.Clear();
        _dockets.Clear();
        lock (_lock)
        {
            _latestTransactionVersion = 0;
            _latestDocketVersion = 0;
            _lastUpdateTime = DateTime.MinValue;
        }
    }
}

/// <summary>
/// A cached transaction.
/// </summary>
public class CachedTransaction
{
    public required string TransactionId { get; init; }
    public required string RegisterId { get; init; }
    public long Version { get; set; }
    public required byte[] Data { get; init; }
    public string? Checksum { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A cached docket.
/// </summary>
public class CachedDocket
{
    public required string RegisterId { get; init; }
    public long Version { get; set; }
    public required byte[] Data { get; init; }
    public required string DocketHash { get; init; }
    public string? PreviousHash { get; init; }
    public required List<string> TransactionIds { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Statistics for a register cache entry.
/// </summary>
public class RegisterCacheStatistics
{
    public string RegisterId { get; init; } = string.Empty;
    public int TransactionCount { get; init; }
    public int DocketCount { get; init; }
    public long LatestTransactionVersion { get; init; }
    public long LatestDocketVersion { get; init; }
    public DateTime LastUpdateTime { get; init; }
}
