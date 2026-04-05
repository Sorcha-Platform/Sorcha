// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.McpServer.Services;

/// <summary>
/// In-memory rate limiting using sliding window algorithm.
/// Limits are driven by <see cref="RateLimitSettings"/> (shared "RateLimiting" config section).
/// </summary>
public sealed class RateLimitService : IRateLimitService, IDisposable
{
    private readonly ILogger<RateLimitService> _logger;
    private readonly RateLimitSettings _settings;
    private readonly ConcurrentDictionary<string, RateLimiter> _userLimiters = new();
    private readonly ConcurrentDictionary<string, RateLimiter> _tenantLimiters = new();
    private readonly ConcurrentDictionary<string, RateLimiter> _adminToolLimiters = new();

    public RateLimitService(
        IOptions<RateLimitSettings> settings,
        ILogger<RateLimitService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public RateLimitResult CheckRateLimit(string userId, string tenantId, string toolCategory)
    {
        // Check user limit
        var userLimiter = _userLimiters.GetOrAdd(userId, _ => CreateSlidingWindowLimiter(_settings.McpPerUserRequestsPerMinute));
        using var userLease = userLimiter.AttemptAcquire();
        if (!userLease.IsAcquired)
        {
            _logger.LogWarning("User {UserId} rate limited", userId);
            return RateLimitResult.Limited(TimeSpan.FromSeconds(60), "User rate limit exceeded");
        }

        // Check tenant limit
        var tenantLimiter = _tenantLimiters.GetOrAdd(tenantId, _ => CreateSlidingWindowLimiter(_settings.McpPerTenantRequestsPerMinute));
        using var tenantLease = tenantLimiter.AttemptAcquire();
        if (!tenantLease.IsAcquired)
        {
            _logger.LogWarning("Tenant {TenantId} rate limited", tenantId);
            return RateLimitResult.Limited(TimeSpan.FromSeconds(60), "Tenant rate limit exceeded");
        }

        // Check admin tool limit if applicable
        if (toolCategory == "admin")
        {
            var adminKey = $"{userId}:admin";
            var adminLimiter = _adminToolLimiters.GetOrAdd(adminKey, _ => CreateSlidingWindowLimiter(_settings.McpAdminToolsRequestsPerMinute));
            using var adminLease = adminLimiter.AttemptAcquire();
            if (!adminLease.IsAcquired)
            {
                _logger.LogWarning("User {UserId} admin tools rate limited", userId);
                return RateLimitResult.Limited(TimeSpan.FromSeconds(60), "Admin tools rate limit exceeded");
            }
        }

        return RateLimitResult.Allowed();
    }

    private static RateLimiter CreateSlidingWindowLimiter(int permitLimit)
    {
        return new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6, // 10-second segments
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // No queuing, fail immediately
        });
    }

    public void Dispose()
    {
        foreach (var limiter in _userLimiters.Values)
        {
            limiter.Dispose();
        }
        foreach (var limiter in _tenantLimiters.Values)
        {
            limiter.Dispose();
        }
        foreach (var limiter in _adminToolLimiters.Values)
        {
            limiter.Dispose();
        }
    }
}
