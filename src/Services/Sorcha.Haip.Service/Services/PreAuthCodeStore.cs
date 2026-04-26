// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Sorcha.AtomicCache;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Stores pre-authorized codes for HAIP credential issuance. Codes are
/// one-time-use with TTL-based expiry. Backed by
/// <see cref="IAtomicDistributedCache"/> so redemption is a single
/// atomic round-trip — two relying-party callbacks racing to redeem the
/// same code resolve to exactly one winner.
/// </summary>
public class PreAuthCodeStore
{
    private readonly IAtomicDistributedCache _cache;
    private readonly HaipNonceMetrics _metrics;
    private readonly ILogger<PreAuthCodeStore> _logger;
    private readonly int _ttlSeconds;

    public PreAuthCodeStore(
        IAtomicDistributedCache cache,
        HaipNonceMetrics metrics,
        ILogger<PreAuthCodeStore> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _cache = cache;
        _metrics = metrics;
        _logger = logger;
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
        await _cache.SetAsync(key, offerId.ToString(), TimeSpan.FromSeconds(_ttlSeconds), ct);

        _logger.LogInformation("Created pre-auth code for offer {OfferId}, TTL={Ttl}s", offerId, _ttlSeconds);
        return code;
    }

    /// <summary>
    /// Redeems a pre-authorized code (one-time-use). Returns the offer ID
    /// or null if the code is invalid, expired, or already redeemed.
    /// Atomic — under N concurrent redeems of the same code, exactly one
    /// returns the offer ID and the rest return null.
    /// </summary>
    public async Task<Guid?> RedeemAsync(string code, CancellationToken ct = default)
    {
        var key = $"haip:preauth:{code}";
        var value = await _cache.GetAndRemoveAsync(key, ct);

        if (value is null || !Guid.TryParse(value, out var offerId))
        {
            _metrics.RecordMiss(HaipNonceMetrics.StoreKind.PreAuth);
            _logger.LogWarning("Pre-auth code redemption failed: code not found, expired, or already redeemed");
            return null;
        }

        _metrics.RecordSuccess(HaipNonceMetrics.StoreKind.PreAuth);
        _logger.LogInformation("Redeemed pre-auth code for offer {OfferId}", offerId);
        return offerId;
    }
}
