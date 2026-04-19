// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for access tokens. Maps token IDs to offer IDs with
/// TTL-based expiry. The in-memory fallback uses MemoryCache with TTL
/// to prevent unbounded growth.
/// </summary>
/// <remarks>
/// TODO: Hash the access token (SHA-256) before using it as the Redis key
/// to prevent token exposure via key enumeration (SCAN).
/// TODO: Implement IDisposable to dispose the MemoryCache fallback (CA2213).
/// </remarks>
public class AccessTokenStore
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<AccessTokenStore> _logger;
    private readonly int _ttlSeconds;

    // In-memory fallback with TTL-based eviction (no unbounded growth)
    private readonly MemoryCache _memoryStore = new(new MemoryCacheOptions());

    public AccessTokenStore(
        ILogger<AccessTokenStore> logger,
        IConfiguration configuration,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _ttlSeconds = configuration.GetValue<int>("Haip:TokenLifetimeSeconds", 300);
    }

    /// <summary>
    /// Stores an access token mapped to the offer ID it was issued for.
    /// </summary>
    public async Task StoreAsync(string accessToken, Guid offerId, CancellationToken ct = default)
    {
        var key = $"haip:token:{accessToken}";
        var value = offerId.ToString();

        if (_cache != null)
        {
            await _cache.SetStringAsync(key, value,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_ttlSeconds) },
                ct);
        }
        else
        {
            _memoryStore.Set(key, value, TimeSpan.FromSeconds(_ttlSeconds));
        }

        _logger.LogDebug("Stored access token for offer {OfferId}, TTL={Ttl}s", offerId, _ttlSeconds);
    }

    /// <summary>
    /// Looks up the offer ID for a given access token.
    /// Returns null if the token is invalid/expired.
    /// </summary>
    public async Task<Guid?> LookupAsync(string accessToken, CancellationToken ct = default)
    {
        var key = $"haip:token:{accessToken}";

        string? value;
        if (_cache != null)
        {
            value = await _cache.GetStringAsync(key, ct);
        }
        else
        {
            _memoryStore.TryGetValue(key, out value);
        }

        if (value == null || !Guid.TryParse(value, out var offerId))
            return null;

        return offerId;
    }
}
