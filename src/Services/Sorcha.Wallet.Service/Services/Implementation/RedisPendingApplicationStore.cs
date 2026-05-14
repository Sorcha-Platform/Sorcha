// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// <see cref="IDistributedCache"/>-backed implementation of
/// <see cref="IPendingApplicationStore"/>. Production resolves to Redis via the
/// existing Wallet Service registration; tests resolve to the in-memory default.
/// 24-hour absolute TTL — the notice is by definition ephemeral, ending in
/// either an explicit clear or expiry (Feature 124, R-001).
/// </summary>
public sealed class RedisPendingApplicationStore : IPendingApplicationStore
{
    private const string KeyPrefix = "sorcha:wallet:pending-app:";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Meter Meter = new("Sorcha.Wallet.Service");
    private static readonly Counter<long> NoticeCounter =
        Meter.CreateCounter<long>("sorcha_pending_application_notice_total");

    private readonly IDistributedCache _cache;

    /// <summary>Initialises a new instance.</summary>
    public RedisPendingApplicationStore(IDistributedCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async Task<PendingApplicationNotice?> GetAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var bytes = await _cache.GetAsync(KeyFor(platformUserId), ct).ConfigureAwait(false);
        NoticeCounter.Add(1, new KeyValuePair<string, object?>("op", "read"));
        if (bytes is null || bytes.Length == 0) return null;
        return JsonSerializer.Deserialize<PendingApplicationNotice>(bytes, JsonOptions);
    }

    /// <inheritdoc />
    public async Task<PendingApplicationNotice> SetAsync(Guid platformUserId, string label, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label must be non-empty.", nameof(label));

        var notice = new PendingApplicationNotice(label, DateTimeOffset.UtcNow);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(notice, JsonOptions);
        await _cache.SetAsync(
            KeyFor(platformUserId),
            bytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl },
            ct).ConfigureAwait(false);
        NoticeCounter.Add(1, new KeyValuePair<string, object?>("op", "set"));
        return notice;
    }

    /// <inheritdoc />
    public async Task ClearAsync(Guid platformUserId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(KeyFor(platformUserId), ct).ConfigureAwait(false);
        NoticeCounter.Add(1, new KeyValuePair<string, object?>("op", "clear"));
    }

    private static string KeyFor(Guid platformUserId) => $"{KeyPrefix}{platformUserId:N}";
}
