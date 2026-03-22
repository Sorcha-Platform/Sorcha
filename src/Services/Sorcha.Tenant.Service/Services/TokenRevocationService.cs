// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Configuration for token revocation service.
/// </summary>
public class TokenRevocationConfiguration
{
    /// <summary>
    /// Maximum failed authentication attempts before rate limiting.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Duration of rate limiting window in seconds.
    /// </summary>
    public int RateLimitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Redis key prefix for revoked tokens.
    /// </summary>
    public string RevokedTokenPrefix { get; set; } = "auth:revoked:";

    /// <summary>
    /// Redis key prefix for user token tracking.
    /// </summary>
    public string UserTokensPrefix { get; set; } = "auth:user_tokens:";

    /// <summary>
    /// Redis key prefix for organization token tracking.
    /// </summary>
    public string OrgTokensPrefix { get; set; } = "auth:org_tokens:";

    /// <summary>
    /// Redis key prefix for failed auth attempts.
    /// </summary>
    public string FailedAttemptsPrefix { get; set; } = "auth:failed:";

    /// <summary>
    /// When true, treat tokens as revoked if Redis is unavailable (fail-closed).
    /// When false, allow tokens through if Redis is unavailable (fail-open).
    /// Default: true (fail-closed) for production safety.
    /// </summary>
    public bool FailClosedOnRedisUnavailable { get; set; } = true;
}

/// <summary>
/// Redis-backed implementation of token revocation service.
/// </summary>
public class TokenRevocationService : ITokenRevocationService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TokenRevocationConfiguration _config;
    private readonly ILogger<TokenRevocationService> _logger;

    public TokenRevocationService(
        IConnectionMultiplexer redis,
        IOptions<TokenRevocationConfiguration> options,
        ILogger<TokenRevocationService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = options?.Value ?? new TokenRevocationConfiguration();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task RevokeTokenAsync(string jti, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            throw new ArgumentException("JTI cannot be null or empty", nameof(jti));
        }

        var db = _redis.GetDatabase();
        var key = $"{_config.RevokedTokenPrefix}{jti}";
        var ttl = expiresAt - DateTimeOffset.UtcNow;

        if (ttl > TimeSpan.Zero)
        {
            await db.StringSetAsync(key, "revoked", ttl);
            _logger.LogInformation("Token {Jti} revoked, TTL: {Ttl}", jti, ttl);
        }
        else
        {
            _logger.LogDebug("Token {Jti} already expired, skipping revocation", jti);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{_config.RevokedTokenPrefix}{jti}";
            return await db.KeyExistsAsync(key);
        }
        catch (RedisConnectionException ex)
        {
            if (_config.FailClosedOnRedisUnavailable)
            {
                _logger.LogWarning(ex, "Redis unavailable for revocation check — REJECTING token {Jti} (fail-closed mode)", jti);
                return true; // Treat as revoked — deny access when revocation state is unknown
            }

            _logger.LogWarning(ex, "Redis unavailable for revocation check — allowing token {Jti} (fail-open mode)", jti);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task RevokeAllUserTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        var db = _redis.GetDatabase();
        var userTokensKey = $"{_config.UserTokensPrefix}{userId}";

        // Get all tracked tokens for this user
        var tokens = await db.SetMembersAsync(userTokensKey);

        foreach (var token in tokens)
        {
            var jti = token.ToString();
            // Revoke with 24h TTL (max refresh token lifetime)
            await db.StringSetAsync(
                $"{_config.RevokedTokenPrefix}{jti}",
                "revoked",
                TimeSpan.FromHours(24));
        }

        // Clear the tracking set
        await db.KeyDeleteAsync(userTokensKey);

        _logger.LogInformation("Revoked {Count} tokens for user {UserId}", tokens.Length, userId);
    }

    /// <inheritdoc/>
    public async Task RevokeAllOrganizationTokensAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new ArgumentException("Organization ID cannot be null or empty", nameof(organizationId));
        }

        var db = _redis.GetDatabase();
        var orgTokensKey = $"{_config.OrgTokensPrefix}{organizationId}";

        // Get all tracked tokens for this organization
        var tokens = await db.SetMembersAsync(orgTokensKey);

        foreach (var token in tokens)
        {
            var jti = token.ToString();
            await db.StringSetAsync(
                $"{_config.RevokedTokenPrefix}{jti}",
                "revoked",
                TimeSpan.FromHours(24));
        }

        await db.KeyDeleteAsync(orgTokensKey);

        _logger.LogInformation("Revoked {Count} tokens for organization {OrgId}", tokens.Length, organizationId);
    }

    /// <inheritdoc/>
    public async Task TrackTokenAsync(
        string jti,
        string userId,
        string? organizationId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var db = _redis.GetDatabase();
        var ttl = expiresAt - DateTimeOffset.UtcNow;

        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        // Track token for user
        var userTokensKey = $"{_config.UserTokensPrefix}{userId}";
        await db.SetAddAsync(userTokensKey, jti);
        await db.KeyExpireAsync(userTokensKey, ttl);

        // Track token for organization if applicable
        if (!string.IsNullOrWhiteSpace(organizationId))
        {
            var orgTokensKey = $"{_config.OrgTokensPrefix}{organizationId}";
            await db.SetAddAsync(orgTokensKey, jti);
            await db.KeyExpireAsync(orgTokensKey, ttl);
        }
    }

    /// <inheritdoc/>
    public async Task<int> IncrementFailedAuthAttemptsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return 0;
        }

        var db = _redis.GetDatabase();
        var key = $"{_config.FailedAttemptsPrefix}{identifier}";

        var count = await db.StringIncrementAsync(key);

        // Set expiry on first increment
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(_config.RateLimitWindowSeconds));
        }

        return (int)count;
    }

    /// <inheritdoc/>
    public async Task<bool> IsRateLimitedAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        try
        {
            var db = _redis.GetDatabase();
            var key = $"{_config.FailedAttemptsPrefix}{identifier}";
            var value = await db.StringGetAsync(key);

            if (value.HasValue && int.TryParse(value.ToString(), out var count))
            {
                return count >= _config.MaxFailedAttempts;
            }

            return false;
        }
        catch (RedisConnectionException ex)
        {
            if (_config.FailClosedOnRedisUnavailable)
            {
                _logger.LogWarning(ex, "Redis unavailable for rate limit check — BLOCKING request (fail-closed mode)");
                return true; // Block when rate limit state is unknown
            }

            _logger.LogWarning(ex, "Redis unavailable for rate limit check — allowing request (fail-open mode)");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ResetFailedAuthAttemptsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        var db = _redis.GetDatabase();
        var key = $"{_config.FailedAttemptsPrefix}{identifier}";
        await db.KeyDeleteAsync(key);
    }
}
