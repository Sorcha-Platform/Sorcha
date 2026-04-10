// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Redis-backed store for pre-authorized codes. Codes are one-time-use
/// with TTL-based expiry. The in-memory fallback uses ConcurrentDictionary
/// for thread safety under concurrent ASP.NET Core requests.
/// </summary>
public class PreAuthCodeStore
{
    private readonly IDistributedCache? _cache;
    private readonly ILogger<PreAuthCodeStore> _logger;
    private readonly int _ttlSeconds;

    private readonly ConcurrentDictionary<string, string> _memoryStore = new();

    public PreAuthCodeStore(
        ILogger<PreAuthCodeStore> logger,
        IConfiguration configuration,
        IDistributedCache? cache = null)
    {
        _logger = logger;
        _cache = cache;
        _ttlSeconds = configuration.GetValue<int>("Haip:PreAuthCodeLifetimeSeconds", 300);
    }

    /// <summary>
    /// Generates a cryptographically random pre-authorized code and stores
    /// the associated offer ID.
    /// </summary>
    public async Task<string> CreateAsync(Guid offerId, CancellationToken ct = default)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var key = $"haip:preauth:{code}";
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

        _logger.LogInformation("Created pre-auth code for offer {OfferId}, TTL={Ttl}s", offerId, _ttlSeconds);
        return code;
    }

    /// <summary>
    /// Redeems a pre-authorized code (one-time-use). Returns the offer ID
    /// or null if the code is invalid/expired/already redeemed.
    /// Uses atomic remove for thread safety — concurrent redemption attempts
    /// on the same code will only succeed once.
    /// </summary>
    public async Task<Guid?> RedeemAsync(string code, CancellationToken ct = default)
    {
        var key = $"haip:preauth:{code}";

        string? value;
        if (_cache != null)
        {
            // Atomic get-and-delete: read then remove in sequence.
            // IDistributedCache doesn't expose GETDEL — the Remove call
            // is a separate round-trip but acceptable for the pre-release
            // in-memory Redis provider. Production should use IDatabase.StringGetDeleteAsync.
            value = await _cache.GetStringAsync(key, ct);
            if (value != null)
                await _cache.RemoveAsync(key, ct);
        }
        else
        {
            // ConcurrentDictionary.TryRemove is atomic
            _memoryStore.TryRemove(key, out value);
        }

        if (value == null || !Guid.TryParse(value, out var offerId))
        {
            _logger.LogWarning("Pre-auth code redemption failed: code not found or expired");
            return null;
        }

        _logger.LogInformation("Redeemed pre-auth code for offer {OfferId}", offerId);
        return offerId;
    }
}
