// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.Blueprint.Engine.Caching;

/// <summary>
/// Cache for parsed JsonSchema objects to avoid re-parsing the same schema on every validation.
/// </summary>
/// <remarks>
/// Thread-safe for concurrent access via per-key locking.
/// Schema parsing (JsonSchema.FromText) is expensive — this cache ensures each unique schema
/// is only parsed once and then reused for subsequent validations.
/// </remarks>
public class JsonSchemaCache : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _defaultOptions;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private bool _disposed;

    public JsonSchemaCache(IMemoryCache cache)
    {
        _cache = cache;
        _defaultOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            SlidingExpiration = TimeSpan.FromMinutes(15),
            Size = 1
        };
    }

    public JsonSchemaCache() : this(new MemoryCache(new MemoryCacheOptions
    {
        SizeLimit = 500 // Schemas are larger than logic expressions
    }))
    {
    }

    /// <summary>
    /// Get a cached parsed schema, or parse and cache it.
    /// </summary>
    public JsonSchema GetOrAdd(JsonNode schemaNode, Func<string, JsonSchema> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var schemaJson = schemaNode.ToJsonString();
        var key = ComputeHash(schemaJson);

        return _cache.GetOrCreate(key, entry =>
        {
            entry.SetOptions(_defaultOptions);
            return factory(schemaJson);
        })!;
    }

    /// <summary>
    /// Get a cached parsed schema, or parse and cache it (async with per-key locking).
    /// </summary>
    public async Task<JsonSchema> GetOrAddAsync(JsonNode schemaNode, Func<string, Task<JsonSchema>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var schemaJson = schemaNode.ToJsonString();
        var key = ComputeHash(schemaJson);

        if (_cache.TryGetValue(key, out JsonSchema? cached))
        {
            return cached!;
        }

        var keyLock = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out cached))
            {
                return cached!;
            }

            var value = await factory(schemaJson);
            _cache.Set(key, value, _defaultOptions);
            return value;
        }
        finally
        {
            keyLock.Release();
        }
    }

    /// <summary>
    /// Try to get a cached schema.
    /// </summary>
    public bool TryGet(JsonNode schemaNode, out JsonSchema? value)
    {
        var key = ComputeHash(schemaNode.ToJsonString());
        return _cache.TryGetValue(key, out value);
    }

    /// <summary>
    /// Clear the entire schema cache.
    /// </summary>
    public void Clear()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }

        _locks.Clear();
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

    private static string ComputeHash(string schemaJson)
    {
        var bytes = Encoding.UTF8.GetBytes(schemaJson);
        var hash = SHA256.HashData(bytes);
        return $"schema:{Convert.ToBase64String(hash)}";
    }
}
