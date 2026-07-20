// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for access tokens. Maps token IDs to offer IDs with
/// TTL-based expiry. The in-memory fallback uses MemoryCache with TTL
/// to prevent unbounded growth.
/// </summary>
/// <remarks>
/// The token is SHA-256 hashed before it is used as a cache key. The key space is the
/// only place the raw bearer token would otherwise come to rest: anyone able to read the
/// Redis keyspace — a <c>SCAN</c>, a memory dump, a managed-Redis console, a support
/// engineer with read access — could previously lift live bearer tokens verbatim and
/// replay them until expiry. Hashing means the keyspace holds only a one-way digest, and
/// lookup still works because the caller presents the token and we hash it the same way.
///
/// This is deliberately a hash and not encryption: nothing ever needs to recover the
/// token from the key, so there is no key to manage and no way to reverse the store.
/// </remarks>
public sealed class AccessTokenStore : IDisposable
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
        var key = KeyFor(accessToken);
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
        var key = KeyFor(accessToken);

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

    /// <summary>
    /// The cache key for a token: <c>haip:token:{sha256-hex}</c>. Never let the raw token
    /// reach the keyspace — see the remarks on <see cref="AccessTokenStore"/>. Hex rather
    /// than base64 so the key is safe in every Redis tool and log without escaping.
    /// </summary>
    private static string KeyFor(string accessToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        return $"haip:token:{Convert.ToHexStringLower(digest)}";
    }

    /// <summary>Disposes the in-memory fallback cache (CA2213).</summary>
    public void Dispose()
    {
        _memoryStore.Dispose();
    }
}
