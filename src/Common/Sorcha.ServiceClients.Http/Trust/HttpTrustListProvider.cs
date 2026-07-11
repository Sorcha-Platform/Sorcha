// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Trust;

/// <summary>
/// Feature 181 US3 (T035) — the verifying-services read path for imported trusted-list anchors:
/// a caching HTTP-backed <see cref="ITrustListProvider"/> over the Tenant Service's service-tier
/// <c>GET /api/v1/trust/trustlists/{id}/anchors</c> endpoint. Replaces the in-memory
/// <see cref="OperatorSnapshotTrustListProvider"/> as the authoritative source for Blueprint / HAIP.
/// A 15-minute in-process cache is tolerated because trusted lists roll monthly (R6). A missing
/// snapshot resolves to null so the trust-list source fails closed (FR-014).
/// </summary>
public sealed class HttpTrustListProvider : ITrustListProvider
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The named <see cref="IHttpClientFactory"/> client whose base address targets the Tenant Service.</summary>
    public const string HttpClientName = "trustlist-provider";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly TimeProvider _clock;
    private readonly ILogger<HttpTrustListProvider> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public HttpTrustListProvider(
        IHttpClientFactory httpClientFactory,
        IServiceAuthClient serviceAuth,
        ILogger<HttpTrustListProvider> logger,
        TimeProvider? clock = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<TrustListSnapshot?> GetSnapshotAsync(string trustListId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trustListId))
        {
            return null;
        }

        var now = _clock.GetUtcNow();
        if (_cache.TryGetValue(trustListId, out var cached) && cached.Expiry > now)
        {
            return cached.Snapshot;
        }

        var snapshot = await FetchAsync(trustListId, cancellationToken).ConfigureAwait(false);
        _cache[trustListId] = new CacheEntry(now.Add(CacheTtl), snapshot);
        return snapshot;
    }

    private async Task<TrustListSnapshot?> FetchAsync(string trustListId, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            await ServiceClientAuthHelper.SetAuthHeaderAsync(client, _serviceAuth, _logger, HttpClientName, ct)
                .ConfigureAwait(false);
            using var resp = await client.GetAsync(
                $"/api/v1/trust/trustlists/{Uri.EscapeDataString(trustListId)}/anchors", ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null; // TRUSTLIST_UNAVAILABLE — fail closed.
            }
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Trust-list anchors read returned {Status} for {TrustListId}", resp.StatusCode, trustListId);
                return null;
            }

            var dto = await resp.Content.ReadFromJsonAsync<AnchorsDto>(JsonOptions, ct).ConfigureAwait(false);
            if (dto is null)
            {
                return null;
            }

            var roots = new List<byte[]>(dto.RootsDerBase64?.Count ?? 0);
            foreach (var b64 in dto.RootsDerBase64 ?? [])
            {
                try { roots.Add(Convert.FromBase64String(b64)); }
                catch (FormatException) { /* skip a malformed root entry */ }
            }

            var freshnessTimestamp = dto.NextUpdate ?? _clock.GetUtcNow();
            return new TrustListSnapshot
            {
                Id = dto.TrustListId ?? trustListId,
                Roots = roots,
                Source = "tenant-trustlist",
                CreatedAt = _clock.GetUtcNow(),
                Freshness = freshnessTimestamp,
                SequenceNumber = dto.SequenceNumber,
                NextUpdate = dto.NextUpdate,
                FreshnessState = dto.Freshness,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Trust-list anchors read failed for {TrustListId}", trustListId);
            return null;
        }
    }

    private readonly record struct CacheEntry(DateTimeOffset Expiry, TrustListSnapshot? Snapshot);

    private sealed record AnchorsDto
    {
        [JsonPropertyName("trustListId")] public string? TrustListId { get; init; }
        [JsonPropertyName("sequenceNumber")] public long SequenceNumber { get; init; }
        [JsonPropertyName("freshness")] public string? Freshness { get; init; }
        [JsonPropertyName("nextUpdate")] public DateTimeOffset? NextUpdate { get; init; }
        [JsonPropertyName("rootsDerBase64")] public List<string>? RootsDerBase64 { get; init; }
    }
}
