// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Redis-backed <see cref="IClaimsFetchTokenStore"/>. Single-use, atomic-remove
/// via Lua. Pattern mirrors the established NonceStore conventions used
/// elsewhere in the platform (e.g. F126's enrol-session JTI sentinel).
/// </summary>
public sealed class RedisClaimsFetchTokenStore : IClaimsFetchTokenStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisClaimsFetchTokenStore> _logger;

    private const string TokenPrefix = "sorcha:presentation:claims-fetch-token:";

    // Lua script for atomic GETDEL. EVAL is more portable than the GETDEL
    // command (Redis 6.2+); this keeps the store usable on slightly older
    // Redis servers in the test stack.
    private const string GetAndRemoveScript = @"
        local value = redis.call('GET', KEYS[1])
        if value then
            redis.call('DEL', KEYS[1])
        end
        return value";

    public RedisClaimsFetchTokenStore(
        IConnectionMultiplexer redis,
        ILogger<RedisClaimsFetchTokenStore> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StoreAsync(
        string token,
        Guid presentationRequestId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        var db = _redis.GetDatabase();
        var stored = await db.StringSetAsync(
            TokenKey(token),
            presentationRequestId.ToString("N"),
            ttl,
            When.NotExists);

        if (!stored)
        {
            // Collision on a high-entropy token is effectively impossible; the
            // SET NX failure means token re-use, which the caller (lifecycle
            // service) must surface as a programming error.
            throw new InvalidOperationException(
                $"Claims-fetch token already exists. Token reuse is a programming error.");
        }

        _logger.LogDebug(
            "Stored claims-fetch token for presentationRequestId {RequestId} (TTL {Ttl})",
            presentationRequestId, ttl);
    }

    public async Task<Guid?> GetAndRemoveAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(
            GetAndRemoveScript,
            new RedisKey[] { TokenKey(token) });

        if (result.IsNull)
        {
            return null;
        }

        var raw = result.ToString();
        if (string.IsNullOrEmpty(raw) || !Guid.TryParseExact(raw, "N", out var requestId))
        {
            _logger.LogWarning(
                "Claims-fetch token decoded to an unparseable value; treating as missing.");
            return null;
        }

        return requestId;
    }

    private static string TokenKey(string token) => TokenPrefix + token;
}
