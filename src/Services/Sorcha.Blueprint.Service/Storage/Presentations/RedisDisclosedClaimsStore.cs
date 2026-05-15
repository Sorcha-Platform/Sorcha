// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Redis-backed <see cref="IDisclosedClaimsStore"/>. Stores the claims as a
/// JSON document at <c>sorcha:presentation:disclosed-claims:{requestId:N}</c>
/// with TTL set by the caller.
/// </summary>
public sealed class RedisDisclosedClaimsStore : IDisclosedClaimsStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisDisclosedClaimsStore> _logger;

    private const string ClaimsPrefix = "sorcha:presentation:disclosed-claims:";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null
    };

    public RedisDisclosedClaimsStore(
        IConnectionMultiplexer redis,
        ILogger<RedisDisclosedClaimsStore> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StoreAsync(
        Guid presentationRequestId,
        IReadOnlyDictionary<string, object> claims,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(claims);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        var json = JsonSerializer.Serialize(claims, SerializerOptions);
        var db = _redis.GetDatabase();
        await db.StringSetAsync(Key(presentationRequestId), json, ttl);

        _logger.LogDebug(
            "Stored disclosed claims for presentationRequestId {RequestId} ({Count} claims, TTL {Ttl})",
            presentationRequestId, claims.Count, ttl);
    }

    public async Task<IReadOnlyDictionary<string, object>?> GetAsync(
        Guid presentationRequestId,
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var raw = await db.StringGetAsync(Key(presentationRequestId));
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                raw.ToString(), SerializerOptions);
            if (dict is null)
            {
                return null;
            }

            // Box JsonElement values as object to match the interface contract;
            // callers re-serialise to JSON for the wire response anyway.
            return dict.ToDictionary(
                kv => kv.Key,
                kv => (object)kv.Value,
                StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Failed to deserialise disclosed claims for {RequestId}; treating as missing",
                presentationRequestId);
            return null;
        }
    }

    private static string Key(Guid requestId) => ClaimsPrefix + requestId.ToString("N");
}
