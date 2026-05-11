// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Polly;
using Polly.CircuitBreaker;

using StackExchange.Redis;

using Sorcha.Register.Models;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// L1+L2 cache fronting <c>IRegisterServiceClient.GetTransactionAsync</c> for the
/// predecessor-lookup hot path (<c>VAL_CHAIN_PREDECESSOR_LOOKUP</c>).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="BlueprintCache"/>: local <see cref="ConcurrentDictionary{TKey, TValue}"/> L1
/// fronted by a Redis L2, with Polly retries + circuit breaker on the L2 path. Drops
/// the BlueprintCache invalidation / warmup / remove APIs because sealed register
/// transactions are immutable — once cached, an entry never has to change.
/// </para>
/// <para>
/// Motivated by the 2026-05-11 validator baseline capture: the rule was 2.26 s
/// aggregate across 720 evaluations (33% of total Total-section time). Every
/// action in a docket batch validates the docket's chain prefix, hitting the
/// same predecessor IDs over and over.
/// </para>
/// </remarks>
public sealed class ChainTransactionCache : IChainTransactionCache
{
    public const string MeterName = "Sorcha.Validator.ChainCache";

    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ChainTransactionCacheConfiguration _config;
    private readonly ILogger<ChainTransactionCache> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly ConcurrentDictionary<string, LocalEntry> _localCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fetchLocks = new();

    // OTel counters
    private readonly Counter<long> _hitCounter;
    private readonly Counter<long> _missCounter;
    private readonly Counter<long> _localHitCounter;
    private readonly Counter<long> _redisHitCounter;

    // Stats
    private long _totalHits;
    private long _totalMisses;
    private long _localCacheHits;
    private long _redisCacheHits;

    public ChainTransactionCache(
        IConnectionMultiplexer redis,
        IOptions<ChainTransactionCacheConfiguration> config,
        IMeterFactory meterFactory,
        ILogger<ChainTransactionCache> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _redis.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        _pipeline = BuildResiliencePipeline();

        var meter = meterFactory.Create(MeterName);
        _hitCounter = meter.CreateCounter<long>("sorcha_validator_chain_cache_hits",
            description: "Predecessor-lookup cache hits (L1 + L2).");
        _missCounter = meter.CreateCounter<long>("sorcha_validator_chain_cache_misses",
            description: "Predecessor-lookup cache misses (MongoDB roundtrip required).");
        _localHitCounter = meter.CreateCounter<long>("sorcha_validator_chain_cache_l1_hits",
            description: "Predecessor-lookup L1 (local) cache hits.");
        _redisHitCounter = meter.CreateCounter<long>("sorcha_validator_chain_cache_l2_hits",
            description: "Predecessor-lookup L2 (Redis) cache hits.");
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = _config.MaxRetries,
                Delay = _config.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }

    private string GetRedisKey(string registerId, string txId) =>
        $"{_config.KeyPrefix}{registerId}:{txId}";

    /// <inheritdoc />
    public Task<TransactionModel?> GetAsync(
        string registerId,
        string txId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(txId);
        return GetCoreAsync(registerId, txId, recordCounters: true, ct);
    }

    /// <summary>
    /// Internal lookup. Used by <see cref="GetOrFetchAsync"/> for its post-lock
    /// double-check without re-incrementing the miss counter (a single
    /// GetOrFetch invocation is logically one cache event regardless of how
    /// many times the underlying read happens).
    /// </summary>
    private async Task<TransactionModel?> GetCoreAsync(
        string registerId,
        string txId,
        bool recordCounters,
        CancellationToken ct)
    {
        if (!_config.Enabled)
            return null;

        // L1 — local
        if (_config.EnableLocalCache && TryGetFromLocalCache(registerId, txId, out var local))
        {
            if (recordCounters)
            {
                Interlocked.Increment(ref _totalHits);
                Interlocked.Increment(ref _localCacheHits);
                _hitCounter.Add(1);
                _localHitCounter.Add(1);
            }
            return local;
        }

        // L2 — Redis
        var redis = await GetFromRedisAsync(registerId, txId, ct);
        if (redis is not null)
        {
            if (recordCounters)
            {
                Interlocked.Increment(ref _totalHits);
                Interlocked.Increment(ref _redisCacheHits);
                _hitCounter.Add(1);
                _redisHitCounter.Add(1);
            }

            if (_config.EnableLocalCache)
                SetInLocalCache(registerId, txId, redis);

            return redis;
        }

        if (recordCounters)
        {
            Interlocked.Increment(ref _totalMisses);
            _missCounter.Add(1);
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<TransactionModel?> GetOrFetchAsync(
        string registerId,
        string txId,
        Func<string, string, CancellationToken, Task<TransactionModel?>> factory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(txId);
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await GetCoreAsync(registerId, txId, recordCounters: true, ct);
        if (cached is not null)
            return cached;

        if (!_config.Enabled)
            return await factory(registerId, txId, ct);

        // Per-key lock to collapse concurrent cold-cache fetches on the same
        // predecessor into a single MongoDB hit (thundering-herd guard).
        var lockKey = $"{registerId}:{txId}";
        var keyLock = _fetchLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock. Don't re-count — a single
            // GetOrFetch is one logical event regardless of the lock dance.
            cached = await GetCoreAsync(registerId, txId, recordCounters: false, ct);
            if (cached is not null)
                return cached;

            var fetched = await factory(registerId, txId, ct);
            if (fetched is not null)
            {
                await SetAsync(registerId, txId, fetched, ct);
            }
            // Don't cache nulls — predecessor may be a not-yet-replicated tx the
            // validator races against; caching null would turn that race into a
            // sticky validation failure.
            return fetched;
        }
        finally
        {
            keyLock.Release();
        }
    }

    /// <inheritdoc />
    public ChainTransactionCacheStats GetStats()
    {
        var now = DateTimeOffset.UtcNow;
        var live = _localCache.Count(kvp => kvp.Value.ExpiresAt > now);

        return new ChainTransactionCacheStats
        {
            TotalHits = Interlocked.Read(ref _totalHits),
            TotalMisses = Interlocked.Read(ref _totalMisses),
            LocalCacheHits = Interlocked.Read(ref _localCacheHits),
            RedisCacheHits = Interlocked.Read(ref _redisCacheHits),
            LocalCacheEntries = live,
        };
    }

    private async Task SetAsync(string registerId, string txId, TransactionModel tx, CancellationToken ct)
    {
        try
        {
            await _pipeline.ExecuteAsync(async _ =>
            {
                var json = JsonSerializer.Serialize(tx, _jsonOptions);
                await _database.StringSetAsync(GetRedisKey(registerId, txId), json, _config.DefaultTtl);
            }, ct);
        }
        catch (Exception ex)
        {
            // Redis-write failures must NOT break validation — the next call will refetch.
            _logger.LogWarning(ex,
                "ChainTransactionCache: failed to write tx {RegisterId}/{TxId} to Redis",
                registerId, txId);
        }

        if (_config.EnableLocalCache)
            SetInLocalCache(registerId, txId, tx);
    }

    private async Task<TransactionModel?> GetFromRedisAsync(
        string registerId, string txId, CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async _ =>
            {
                var json = await _database.StringGetAsync(GetRedisKey(registerId, txId));
                if (json.IsNullOrEmpty)
                    return null;
                return JsonSerializer.Deserialize<TransactionModel>(json.ToString(), _jsonOptions);
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogDebug("ChainTransactionCache: circuit open, skipping Redis");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ChainTransactionCache: Redis error for {RegisterId}/{TxId}",
                registerId, txId);
            return null;
        }
    }

    private bool TryGetFromLocalCache(string registerId, string txId, out TransactionModel? tx)
    {
        tx = null;
        var key = $"{registerId}:{txId}";
        if (!_localCache.TryGetValue(key, out var entry))
            return false;

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _localCache.TryRemove(key, out _);
            return false;
        }

        tx = entry.Transaction;
        return true;
    }

    private void SetInLocalCache(string registerId, string txId, TransactionModel tx)
    {
        if (_localCache.Count >= _config.LocalCacheMaxEntries)
        {
            var overflow = _localCache.Count - _config.LocalCacheMaxEntries + 1;
            var toRemove = _localCache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(overflow)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var k in toRemove)
                _localCache.TryRemove(k, out _);
        }

        _localCache[$"{registerId}:{txId}"] = new LocalEntry
        {
            Transaction = tx,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_config.LocalCacheTtl),
        };
    }

    private sealed class LocalEntry
    {
        public required TransactionModel Transaction { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
