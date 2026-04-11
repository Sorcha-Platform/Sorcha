// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for access tokens. Maps token IDs to offer IDs with
/// TTL-based expiry. The in-memory fallback uses ConcurrentDictionary
/// for thread safety under concurrent ASP.NET Core requests.
/// </summary>
public class AccessTokenStore
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<AccessTokenStore> _logger;
    private readonly int _ttlSeconds;

    private readonly ConcurrentDictionary<string, string> _memoryStore = new();

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
            _memoryStore[key] = value;
        }

        _logger.LogInformation("Stored access token for offer {OfferId}, TTL={Ttl}s", offerId, _ttlSeconds);
    }

    /// <summary>
    /// Looks up the offer ID associated with an access token.
    /// Returns null if the token is invalid or expired.
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
        {
            return null;
        }

        return offerId;
    }
}
