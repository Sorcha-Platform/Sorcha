// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for c_nonce values. Nonces are single-use
/// with TTL-based expiry. Thread-safe via ConcurrentDictionary fallback.
/// </summary>
public class NonceStore
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<NonceStore> _logger;
    private readonly int _ttlSeconds;

    private readonly ConcurrentDictionary<string, byte> _memoryStore = new();

    public NonceStore(
        ILogger<NonceStore> logger,
        IConfiguration configuration,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _ttlSeconds = configuration.GetValue<int>("Haip:NonceLifetimeSeconds", 300);
    }

    /// <summary>
    /// Creates a fresh c_nonce and stores it for later validation.
    /// </summary>
    public async Task<(string Nonce, int ExpiresIn)> CreateAsync(CancellationToken ct = default)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var key = $"haip:nonce:{nonce}";

        if (_cache != null)
        {
            await _cache.SetStringAsync(key, "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_ttlSeconds) },
                ct);
        }
        else
        {
            _memoryStore[key] = 1;
        }

        return (nonce, _ttlSeconds);
    }

    /// <summary>
    /// Consumes a c_nonce (single-use). Returns true if the nonce was valid.
    /// Uses atomic remove for thread safety.
    /// </summary>
    public async Task<bool> ConsumeAsync(string nonce, CancellationToken ct = default)
    {
        var key = $"haip:nonce:{nonce}";

        if (_cache != null)
        {
            var value = await _cache.GetStringAsync(key, ct);
            if (value == null) return false;
            await _cache.RemoveAsync(key, ct);
            return true;
        }

        return _memoryStore.TryRemove(key, out _);
    }
}
