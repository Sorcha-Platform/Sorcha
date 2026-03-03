// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Sliding window rate limiter for notification delivery using Redis INCR with TTL.
/// Caps at a configurable rate per user per minute (default: 10/min).
/// Rate-limited notifications overflow to digest delivery.
/// </summary>
public sealed class NotificationRateLimiter : INotificationRateLimiter
{
    private const string KeyPrefix = "wallet:ratelimit:";
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(60);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NotificationRateLimiter> _logger;
    private readonly int _maxPerMinute;

    public NotificationRateLimiter(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<NotificationRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
        _maxPerMinute = configuration.GetValue("Notifications:RealTimeRateLimitPerMinute", 10);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{KeyPrefix}{userId}";

        var count = await db.StringIncrementAsync(key);

        // Set TTL on first increment (new window)
        if (count == 1)
        {
            await db.KeyExpireAsync(key, WindowDuration);
        }

        if (count <= _maxPerMinute)
        {
            return true;
        }

        _logger.LogDebug(
            "Rate limit reached for user {UserId}: {Count}/{Max} in current window",
            userId, count, _maxPerMinute);
        return false;
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"{KeyPrefix}{userId}";
        var value = await db.StringGetAsync(key);
        return value.HasValue ? (int)value : 0;
    }
}
