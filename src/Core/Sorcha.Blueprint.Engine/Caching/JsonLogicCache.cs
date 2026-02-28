// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.Blueprint.Engine.Caching;

/// <summary>
/// Cache for compiled/parsed JSON Logic expressions
/// </summary>
/// <remarks>
/// Caches expressions by their hash and type to avoid re-parsing the same expression multiple times.
/// Thread-safe for concurrent async access via per-key locking.
/// </remarks>
public class JsonLogicCache : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _keysByHash = new();
    private bool _disposed;

    public JsonLogicCache(IMemoryCache cache)
    {
        _cache = cache;
        _defaultOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            SlidingExpiration = TimeSpan.FromMinutes(15),
            Size = 1 // Each entry counts as 1 unit
        };
    }

    /// <summary>
    /// Creates a new JsonLogicCache with a configurable maximum size.
    /// </summary>
    /// <param name="maxCacheSize">
    /// Maximum number of cached expressions. Default: 1000.
    /// </param>
    public JsonLogicCache(int maxCacheSize = 1000) : this(new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = maxCacheSize
    }))
    {
    }

    /// <summary>
    /// Get or add an expression to the cache
    /// </summary>
    /// <typeparam name="T">Type of the cached value</typeparam>
    /// <param name="expression">The JSON Logic expression</param>
    /// <param name="factory">Factory function to create the value if not cached</param>
    /// <returns>The cached or newly created value</returns>
    public T GetOrAdd<T>(JsonNode expression, Func<JsonNode, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var key = ComputeTypedKey<T>(expression);
        TrackKey(expression, key);

        return _cache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(_defaultOptions);
            return factory(expression);
        })!;
    }

    /// <summary>
    /// Get or add an expression to the cache asynchronously.
    /// Uses per-key locking to ensure the factory is called at most once per key.
    /// </summary>
    public async Task<T> GetOrAddAsync<T>(JsonNode expression, Func<JsonNode, Task<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var key = ComputeTypedKey<T>(expression);

        if (_cache.TryGetValue(key, out T? cached))
        {
            return cached!;
        }

        // Per-key lock ensures only one factory invocation per expression+type
        var keyLock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cache.TryGetValue(key, out cached))
            {
                return cached!;
            }

            var value = await factory(expression);
            _cache.Set(key, value, _defaultOptions);
            TrackKey(expression, key);
            return value;
        }
        finally
        {
            keyLock.Release();
        }
    }

    /// <summary>
    /// Try to get a cached value
    /// </summary>
    public bool TryGet<T>(JsonNode expression, out T? value)
    {
        var key = ComputeTypedKey<T>(expression);
        return _cache.TryGetValue(key, out value);
    }

    /// <summary>
    /// Clear the cache
    /// </summary>
    public void Clear()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0); // Remove all entries
        }

        _locks.Clear();
        _keysByHash.Clear();
    }

    /// <summary>
    /// Remove a specific expression from the cache for all types
    /// </summary>
    public void Remove(JsonNode expression)
    {
        var hash = ComputeHash(expression);
        if (_keysByHash.TryRemove(hash, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key);
                _locks.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Remove a specific expression+type from the cache
    /// </summary>
    public void Remove<T>(JsonNode expression)
    {
        var key = ComputeTypedKey<T>(expression);
        _cache.Remove(key);
        _locks.TryRemove(key, out _);
    }

    private void TrackKey(JsonNode expression, string typedKey)
    {
        var hash = ComputeHash(expression);
        var bag = _keysByHash.GetOrAdd(hash, _ => []);
        bag.Add(typedKey);
    }

    /// <summary>
    /// Compute a type-aware cache key: hash + type name
    /// </summary>
    private string ComputeTypedKey<T>(JsonNode expression)
    {
        return $"{ComputeHash(expression)}:{typeof(T).FullName}";
    }

    /// <summary>
    /// Compute a hash for the expression
    /// </summary>
    private static string ComputeHash(JsonNode expression)
    {
        var json = expression.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Disposes all per-key semaphores and the underlying MemoryCache.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();

        if (_cache is IDisposable disposableCache)
        {
            disposableCache.Dispose();
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        // Note: MemoryCache doesn't expose hit/miss counters directly
        // For production, consider using a cache with built-in metrics
        return new CacheStatistics
        {
            Message = "Cache statistics require instrumentation. Consider using distributed cache with metrics."
        };
    }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long TotalRequests => Hits + Misses;
    public double HitRate => TotalRequests > 0 ? (double)Hits / TotalRequests : 0;
    public string? Message { get; set; }
}

/// <summary>
/// Cached expression wrapper
/// </summary>
/// <typeparam name="T">Type of the cached expression data</typeparam>
public class CachedExpression<T>
{
    /// <summary>
    /// The original expression
    /// </summary>
    public JsonNode Expression { get; set; } = null!;

    /// <summary>
    /// The cached data
    /// </summary>
    public T Data { get; set; } = default!;

    /// <summary>
    /// When this was cached
    /// </summary>
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of times this has been accessed
    /// </summary>
    public int AccessCount { get; set; }
}
