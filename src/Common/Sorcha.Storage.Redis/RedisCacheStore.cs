// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.Redis;

/// <summary>
/// Redis implementation of ICacheStore for hot-tier caching.
/// Provides resilient cache operations with circuit breaker pattern.
/// </summary>
public class RedisCacheStore : ICacheStore, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultExpiration;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly bool _ownsConnection;

    // Statistics tracking
    private long _totalRequests;
    private long _hits;
    private long _misses;
    private long _evictions;
    private readonly Queue<double> _latencies = new();
    private readonly object _statsLock = new();

    /// <summary>
    /// Initializes a new instance of the RedisCacheStore with configuration options.
    /// </summary>
    /// <param name="options">Hot tier configuration options.</param>
    public RedisCacheStore(IOptions<HotTierConfiguration> options)
        : this(options.Value)
    {
    }

    /// <summary>
    /// Initializes a new instance of the RedisCacheStore with direct configuration.
    /// </summary>
    /// <param name="configuration">Hot tier configuration.</param>
    public RedisCacheStore(HotTierConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Redis, nameof(configuration.Redis));

        var redisConfig = configuration.Redis;
        _keyPrefix = redisConfig.InstanceName;
        _defaultExpiration = configuration.DefaultTtl;

        var configOptions = ConfigurationOptions.Parse(redisConfig.ConnectionString);
        configOptions.ConnectTimeout = redisConfig.ConnectTimeout;
        configOptions.SyncTimeout = redisConfig.SyncTimeout;
        configOptions.AbortOnConnectFail = false;

        _redis = ConnectionMultiplexer.Connect(configOptions);
        _database = _redis.GetDatabase();
        _ownsConnection = true;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _pipeline = BuildResiliencePipeline(redisConfig.CircuitBreaker);
    }

    /// <summary>
    /// Initializes a new instance of the RedisCacheStore with an existing connection.
    /// </summary>
    /// <param name="connection">Existing Redis connection.</param>
    /// <param name="keyPrefix">Key prefix for cache entries.</param>
    /// <param name="defaultExpiration">Default TTL for entries.</param>
    /// <param name="circuitBreakerConfig">Optional circuit breaker configuration.</param>
    public RedisCacheStore(
        IConnectionMultiplexer connection,
        string keyPrefix = "sorcha:",
        TimeSpan? defaultExpiration = null,
        CircuitBreakerConfiguration? circuitBreakerConfig = null)
    {
        _redis = connection ?? throw new ArgumentNullException(nameof(connection));
        _database = _redis.GetDatabase();
        _keyPrefix = keyPrefix;
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(15);
        _ownsConnection = false;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        _pipeline = BuildResiliencePipeline(circuitBreakerConfig);
    }

    private static ResiliencePipeline BuildResiliencePipeline(CircuitBreakerConfiguration? config)
    {
        config ??= new CircuitBreakerConfiguration();

        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = config.FailureThreshold,
                SamplingDuration = config.SamplingDuration,
                BreakDuration = config.BreakDuration
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();
    }

    private string GetKey(string key) => $"{_keyPrefix}{key}";

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalRequests);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _pipeline.ExecuteAsync(async ct =>
            {
                var value = await _database.StringGetAsync(GetKey(key));
                return value;
            }, cancellationToken);

            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);

            if (result.IsNullOrEmpty)
            {
                Interlocked.Increment(ref _misses);
                return default;
            }

            Interlocked.Increment(ref _hits);
            return JsonSerializer.Deserialize<T>(result!, _jsonOptions);
        }
        catch (BrokenCircuitException)
        {
            Interlocked.Increment(ref _misses);
            return default;
        }
        catch (TimeoutException)
        {
            Interlocked.Increment(ref _misses);
            return default;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        var ttl = expiration ?? _defaultExpiration;

        try
        {
            await _pipeline.ExecuteAsync(async ct =>
            {
                await _database.StringSetAsync(GetKey(key), json, ttl);
            }, cancellationToken);

            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);
        }
        catch (BrokenCircuitException)
        {
            // Silently fail on broken circuit - cache is not critical
        }
        catch (TimeoutException)
        {
            // Silently fail on timeout - cache is not critical
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _pipeline.ExecuteAsync(async ct =>
            {
                return await _database.KeyDeleteAsync(GetKey(key));
            }, cancellationToken);

            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);

            if (result)
            {
                Interlocked.Increment(ref _evictions);
            }

            return result;
        }
        catch (BrokenCircuitException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(async ct =>
            {
                return await _database.KeyExistsAsync(GetKey(key));
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        // Try to get from cache first
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Cache miss - call factory
        var value = await factory(cancellationToken);

        // Store in cache
        await SetAsync(key, value, expiration, cancellationToken);

        return value;
    }

    /// <inheritdoc/>
    public async Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        long removed = 0;

        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server is null)
            {
                return 0;
            }

            var fullPattern = GetKey(pattern);
            var keys = server.Keys(pattern: fullPattern).ToArray();

            if (keys.Length > 0)
            {
                await _pipeline.ExecuteAsync(async ct =>
                {
                    removed = await _database.KeyDeleteAsync(keys);
                }, cancellationToken);

                Interlocked.Add(ref _evictions, removed);
            }

            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);

            return removed;
        }
        catch (BrokenCircuitException)
        {
            return 0;
        }
        catch (TimeoutException)
        {
            return 0;
        }
    }

    /// <inheritdoc/>
    public async Task<long> IncrementAsync(
        string key,
        long delta = 1,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _pipeline.ExecuteAsync(async ct =>
            {
                var newValue = await _database.StringIncrementAsync(GetKey(key), delta);

                // Set expiration on first increment (if value equals delta, it was created)
                if (expiration.HasValue && newValue == delta)
                {
                    await _database.KeyExpireAsync(GetKey(key), expiration);
                }

                return newValue;
            }, cancellationToken);

            sw.Stop();
            RecordLatency(sw.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (BrokenCircuitException)
        {
            return delta; // Return expected value on circuit break
        }
        catch (TimeoutException)
        {
            return delta;
        }
    }

    /// <inheritdoc/>
    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        double avgLatency;
        double p99Latency;
        long currentCount = 0;

        lock (_statsLock)
        {
            avgLatency = _latencies.Count > 0 ? _latencies.Average() : 0;
            if (_latencies.Count > 0)
            {
                var sorted = _latencies.OrderBy(l => l).ToList();
                var index = (int)(sorted.Count * 0.99);
                p99Latency = sorted[Math.Min(index, sorted.Count - 1)];
            }
            else
            {
                p99Latency = 0;
            }
        }

        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server is not null)
            {
                currentCount = server.Keys(pattern: $"{_keyPrefix}*").LongCount();
            }
        }
        catch
        {
            // Ignore errors when counting keys
        }

        var stats = new CacheStatistics(
            TotalRequests: Interlocked.Read(ref _totalRequests),
            Hits: Interlocked.Read(ref _hits),
            Misses: Interlocked.Read(ref _misses),
            AverageLatencyMs: avgLatency,
            P99LatencyMs: p99Latency,
            CurrentEntryCount: currentCount,
            EvictionCount: Interlocked.Read(ref _evictions));

        return Task.FromResult(stats);
    }

    private void RecordLatency(double latencyMs)
    {
        lock (_statsLock)
        {
            _latencies.Enqueue(latencyMs);

            // Keep only the last 1000 samples
            if (_latencies.Count > 1000)
            {
                _latencies.Dequeue();
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsConnection && _redis is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_ownsConnection)
        {
            _redis.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
