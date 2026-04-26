// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using Sorcha.AtomicCache;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Stores c_nonce values for HAIP credential issuance. Nonces are
/// single-use with TTL-based expiry. Backed by
/// <see cref="IAtomicDistributedCache"/> so consumption is a single
/// atomic round-trip — concurrent consumers of the same nonce can no
/// longer both succeed (the documented Get+Remove TOCTOU window from
/// the previous <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// implementation is closed).
/// </summary>
public class NonceStore
{
    private readonly IAtomicDistributedCache _cache;
    private readonly HaipNonceMetrics _metrics;
    private readonly ILogger<NonceStore> _logger;
    private readonly int _ttlSeconds;

    public NonceStore(
        IAtomicDistributedCache cache,
        HaipNonceMetrics metrics,
        ILogger<NonceStore> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        _cache = cache;
        _metrics = metrics;
        _logger = logger;
        _ttlSeconds = configuration.GetValue<int>("Haip:NonceLifetimeSeconds", 300);
    }

    /// <summary>
    /// Creates a fresh c_nonce and stores it for later validation.
    /// </summary>
    public async Task<(string Nonce, int ExpiresIn)> CreateAsync(CancellationToken ct = default)
    {
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var key = $"haip:nonce:{nonce}";

        await _cache.SetAsync(key, "1", TimeSpan.FromSeconds(_ttlSeconds), ct);

        return (nonce, _ttlSeconds);
    }

    /// <summary>
    /// Consumes a c_nonce (single-use). Returns true if the nonce was
    /// valid. Atomic — under N concurrent consumes of the same nonce,
    /// exactly one returns true and the rest return false.
    /// </summary>
    public async Task<bool> ConsumeAsync(string nonce, CancellationToken ct = default)
    {
        var key = $"haip:nonce:{nonce}";
        var value = await _cache.GetAndRemoveAsync(key, ct);
        var success = value is not null;

        if (success)
        {
            _metrics.RecordSuccess(HaipNonceMetrics.StoreKind.Nonce);
        }
        else
        {
            _metrics.RecordMiss(HaipNonceMetrics.StoreKind.Nonce);
            _logger.LogWarning("Nonce consume failed: nonce not found, expired, or already consumed");
        }

        return success;
    }
}
