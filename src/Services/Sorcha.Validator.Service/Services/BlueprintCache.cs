// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed blueprint cache with optional local L1 cache.
/// Provides fast blueprint lookups during transaction validation.
/// </summary>
public class BlueprintCache : IBlueprintCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly BlueprintCacheConfiguration _config;
    private readonly ILogger<BlueprintCache> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    // L1 local cache
    private readonly ConcurrentDictionary<string, LocalCacheEntry> _localCache = new();

    // Statistics
    private long _totalHits;
    private long _totalMisses;
    private long _localCacheHits;
    private long _redisCacheHits;
    private readonly List<double> _latencies = [];
    private readonly object _statsLock = new();

    public BlueprintCache(
        IConnectionMultiplexer redis,
        IOptions<BlueprintCacheConfiguration> config,
        ILogger<BlueprintCache> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _redis.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _pipeline = BuildResiliencePipeline();
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = _config.MaxRetries,
                Delay = _config.RetryDelay,
                BackoffType = DelayBackoffType.Exponential
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }


    private string GetBlueprintKey(string blueprintId) =>
        $"{_config.KeyPrefix}{blueprintId}";

    private string GetRegisterIndexKey(string registerId) =>
        $"{_config.KeyPrefix}index:{registerId}";


    /// <inheritdoc/>
    public async Task<BlueprintModel?> GetBlueprintAsync(
        string blueprintId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        var sw = Stopwatch.StartNew();

        try
        {
            // Check L1 local cache first
            if (_config.EnableLocalCache && TryGetFromLocalCache(blueprintId, out var localBlueprint))
            {
                Interlocked.Increment(ref _totalHits);
                Interlocked.Increment(ref _localCacheHits);
                RecordLatency(sw.Elapsed.TotalMilliseconds);
                return localBlueprint;
            }

            // Check L2 Redis cache
            var blueprint = await GetFromRedisAsync(blueprintId, ct);

            if (blueprint != null)
            {
                Interlocked.Increment(ref _totalHits);
                Interlocked.Increment(ref _redisCacheHits);

                // Populate L1 cache
                if (_config.EnableLocalCache)
                {
                    SetInLocalCache(blueprintId, blueprint);
                }
            }
            else
            {
                Interlocked.Increment(ref _totalMisses);
            }

            RecordLatency(sw.Elapsed.TotalMilliseconds);
            return blueprint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blueprint {BlueprintId} from cache", blueprintId);
            Interlocked.Increment(ref _totalMisses);
            RecordLatency(sw.Elapsed.TotalMilliseconds);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> GetOrFetchAsync(
        string blueprintId,
        Func<string, CancellationToken, Task<BlueprintModel?>> factory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentNullException.ThrowIfNull(factory);

        // Try cache first
        var blueprint = await GetBlueprintAsync(blueprintId, ct);

        if (blueprint != null)
            return blueprint;

        // Fetch from source
        _logger.LogDebug("Blueprint {BlueprintId} not in cache, fetching from source", blueprintId);

        blueprint = await factory(blueprintId, ct);

        if (blueprint != null)
        {
            await SetBlueprintAsync(blueprint, ct: ct);
        }

        return blueprint;
    }

    /// <inheritdoc/>
    public async Task SetBlueprintAsync(
        BlueprintModel blueprint,
        TimeSpan? ttl = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprint.Id);

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetBlueprintKey(blueprint.Id);
                var json = JsonSerializer.Serialize(blueprint, _jsonOptions);
                var expiry = ttl ?? _config.DefaultTtl;

                await _database.StringSetAsync(key, json, expiry);

                _logger.LogDebug(
                    "Cached blueprint {BlueprintId} with TTL {Ttl}",
                    blueprint.Id, expiry);
            }, ct);

            // Update L1 cache
            if (_config.EnableLocalCache)
            {
                SetInLocalCache(blueprint.Id, blueprint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache blueprint {BlueprintId}", blueprint.Id);
        }
    }

    /// <inheritdoc/>
    public async Task<ActionModel?> GetActionAsync(
        string blueprintId,
        int actionId,
        CancellationToken ct = default)
    {
        var blueprint = await GetBlueprintAsync(blueprintId, ct);

        return blueprint?.Actions.FirstOrDefault(a => a.Id == actionId);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string blueprintId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        // Check L1 first
        if (_config.EnableLocalCache && _localCache.ContainsKey(blueprintId))
        {
            var entry = _localCache[blueprintId];
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
                return true;
        }

        // Check L2
        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetBlueprintKey(blueprintId);
                return await _database.KeyExistsAsync(key);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of blueprint {BlueprintId}", blueprintId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(
        string blueprintId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);

        // Remove from L1
        _localCache.TryRemove(blueprintId, out _);

        // Remove from L2
        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetBlueprintKey(blueprintId);
                var removed = await _database.KeyDeleteAsync(key);

                if (removed)
                {
                    _logger.LogDebug("Removed blueprint {BlueprintId} from cache", blueprintId);
                }

                return removed;
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove blueprint {BlueprintId} from cache", blueprintId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<long> InvalidateByRegisterAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        long removed = 0;

        try
        {
            // Get all blueprints associated with the register from the index
            var indexKey = GetRegisterIndexKey(registerId);
            var blueprintIds = await _database.SetMembersAsync(indexKey);

            foreach (var id in blueprintIds)
            {
                var blueprintId = id.ToString();

                // Remove from L1
                _localCache.TryRemove(blueprintId, out _);

                // Remove from L2
                var key = GetBlueprintKey(blueprintId);
                if (await _database.KeyDeleteAsync(key))
                {
                    removed++;
                }
            }

            // Clear the index
            await _database.KeyDeleteAsync(indexKey);

            _logger.LogInformation(
                "Invalidated {Count} blueprints for register {RegisterId}",
                removed, registerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate blueprints for register {RegisterId}", registerId);
        }

        return removed;
    }

    /// <inheritdoc/>
    public Task<BlueprintCacheStats> GetStatsAsync(CancellationToken ct = default)
    {
        double avgLatency;
        long redisEntries = 0;

        lock (_statsLock)
        {
            avgLatency = _latencies.Count > 0 ? _latencies.Average() : 0;
        }

        // Count local cache entries (excluding expired)
        var now = DateTimeOffset.UtcNow;
        var localEntries = _localCache.Count(kvp => kvp.Value.ExpiresAt > now);

        // Try to count Redis entries (best effort)
        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server != null)
            {
                redisEntries = server.Keys(pattern: $"{_config.KeyPrefix}*").LongCount();
            }
        }
        catch
        {
            // Ignore errors counting keys
        }

        var stats = new BlueprintCacheStats
        {
            TotalHits = Interlocked.Read(ref _totalHits),
            TotalMisses = Interlocked.Read(ref _totalMisses),
            LocalCacheHits = Interlocked.Read(ref _localCacheHits),
            RedisCacheHits = Interlocked.Read(ref _redisCacheHits),
            LocalCacheEntries = localEntries,
            RedisCacheEntries = redisEntries,
            AverageLatencyMs = avgLatency
        };

        return Task.FromResult(stats);
    }

    /// <inheritdoc/>
    public async Task<int> WarmupAsync(
        string registerId,
        IEnumerable<string> blueprintIds,
        Func<string, CancellationToken, Task<BlueprintModel?>> factory,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(blueprintIds);
        ArgumentNullException.ThrowIfNull(factory);

        var cached = 0;
        var indexKey = GetRegisterIndexKey(registerId);

        foreach (var blueprintId in blueprintIds)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                // Skip if already cached
                if (await ExistsAsync(blueprintId, ct))
                {
                    cached++;
                    continue;
                }

                // Fetch and cache
                var blueprint = await factory(blueprintId, ct);

                if (blueprint != null)
                {
                    await SetBlueprintAsync(blueprint, ct: ct);

                    // Add to register index
                    await _database.SetAddAsync(indexKey, blueprintId);

                    cached++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up blueprint {BlueprintId}", blueprintId);
            }
        }

        _logger.LogInformation(
            "Warmed up {Count} blueprints for register {RegisterId}",
            cached, registerId);

        return cached;
    }


    private async Task<BlueprintModel?> GetFromRedisAsync(string blueprintId, CancellationToken ct)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var key = GetBlueprintKey(blueprintId);
                var json = await _database.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                    return null;

                return JsonSerializer.Deserialize<BlueprintModel>(json.ToString(), _jsonOptions);
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker open, cannot fetch blueprint {BlueprintId}", blueprintId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error fetching blueprint {BlueprintId}", blueprintId);
            return null;
        }
    }

    private bool TryGetFromLocalCache(string blueprintId, out BlueprintModel? blueprint)
    {
        blueprint = null;

        if (!_localCache.TryGetValue(blueprintId, out var entry))
            return false;

        // Check if expired
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _localCache.TryRemove(blueprintId, out _);
            return false;
        }

        blueprint = entry.Blueprint;
        return true;
    }

    private void SetInLocalCache(string blueprintId, BlueprintModel blueprint)
    {
        // Enforce max entries limit
        if (_localCache.Count >= _config.LocalCacheMaxEntries)
        {
            // Remove oldest entries
            var toRemove = _localCache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(_localCache.Count - _config.LocalCacheMaxEntries + 1)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _localCache.TryRemove(key, out _);
            }
        }

        var entry = new LocalCacheEntry
        {
            Blueprint = blueprint,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_config.LocalCacheTtl)
        };

        _localCache[blueprintId] = entry;
    }

    private void RecordLatency(double latencyMs)
    {
        lock (_statsLock)
        {
            _latencies.Add(latencyMs);

            // Keep only the last 1000 samples
            if (_latencies.Count > 1000)
            {
                _latencies.RemoveAt(0);
            }
        }
    }



    private class LocalCacheEntry
    {
        public required BlueprintModel Blueprint { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }

}
