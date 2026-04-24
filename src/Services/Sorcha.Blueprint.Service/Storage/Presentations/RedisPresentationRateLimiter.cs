// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Sliding-window rate limiter via Redis INCR + TTL on the first increment.
/// Scope: per-wallet-per-register. Threshold and window configurable.
/// </summary>
public sealed class RedisPresentationRateLimiter : IPresentationRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<RedisPresentationRateLimiter> _logger;

    private const string KeyPrefix = "sorcha:presentation:ratelimit:";

    public RedisPresentationRateLimiter(
        IConnectionMultiplexer redis,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<RedisPresentationRateLimiter> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PresentationRateLimitResult> CheckAsync(string walletAddress, string registerId, CancellationToken ct = default)
    {
        var opts = _options.Value.RateLimit;
        var db = _redis.GetDatabase();
        var key = $"{KeyPrefix}{walletAddress}:{registerId}";

        // INCR is atomic; set TTL only on the first increment (fresh window).
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(opts.WindowSeconds));
        }

        if (count > opts.Threshold)
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            _logger.LogWarning(
                "Presentation rate limit hit for wallet={Wallet} register={Register} count={Count} threshold={Threshold}",
                walletAddress, registerId, count, opts.Threshold);
            return new PresentationRateLimitResult(
                Allowed: false,
                CurrentCount: count,
                Threshold: opts.Threshold,
                RetryAfter: ttl ?? TimeSpan.FromSeconds(opts.WindowSeconds));
        }

        return new PresentationRateLimitResult(
            Allowed: true,
            CurrentCount: count,
            Threshold: opts.Threshold,
            RetryAfter: null);
    }
}
