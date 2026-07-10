// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Feature 181 (T014) — short-TTL store for served OpenID4VP Request Objects. The
/// F127 sorcha-wallet consumer builds the request object (carrying <c>dcql_query</c>)
/// at initiation; the wallet fetches it via the anonymous
/// <c>GET /api/presentations/{id}/request-object</c> route. NOT single-use — a wallet
/// may retry the fetch within the validity window.
/// </summary>
public interface IRequestObjectStore
{
    /// <summary>Store the request-object JWT for a presentation request.</summary>
    Task StoreAsync(Guid presentationRequestId, string requestObjectJwt, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Read the request-object JWT; null when unknown or expired.</summary>
    Task<string?> GetAsync(Guid presentationRequestId, CancellationToken ct = default);
}

/// <summary>Redis-backed <see cref="IRequestObjectStore"/> (plain GET/SET + TTL).</summary>
public sealed class RedisRequestObjectStore : IRequestObjectStore
{
    private readonly IConnectionMultiplexer _redis;

    private const string KeyPrefix = "sorcha:presentation:request-object:";

    /// <summary>Initialises a new instance.</summary>
    public RedisRequestObjectStore(IConnectionMultiplexer redis)
        => _redis = redis ?? throw new ArgumentNullException(nameof(redis));

    /// <inheritdoc />
    public async Task StoreAsync(Guid presentationRequestId, string requestObjectJwt, TimeSpan ttl, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestObjectJwt);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }

        var db = _redis.GetDatabase();
        await db.StringSetAsync(Key(presentationRequestId), requestObjectJwt, ttl);
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(Key(presentationRequestId));
        return value.HasValue ? value.ToString() : null;
    }

    private static string Key(Guid id) => $"{KeyPrefix}{id:N}";
}
