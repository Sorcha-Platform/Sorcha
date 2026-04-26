// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Default <see cref="IStatusListCache"/>. In-memory cache keyed by status list
/// URI; entries hold the decoded bitstring and the JWT's <c>exp</c> for
/// freshness checks. Concurrent — multiple presentations against the same list
/// share a single decode.
/// </summary>
public sealed class StatusListCache : IStatusListCache
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StatusListCache> _logger;

    private readonly ConcurrentDictionary<string, CachedList> _entries = new();

    /// <summary>Initialises a new instance of the <see cref="StatusListCache"/> class.</summary>
    public StatusListCache(HttpClient httpClient, ILogger<StatusListCache> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string statusListUri, int index, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusListUri);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        var entry = await GetOrFetchAsync(statusListUri, ct);
        if (entry is null)
        {
            // Could not fetch and nothing in cache — fail closed (treat as revoked)
            // would be too aggressive (offline verifier). Caller decides; we
            // surface "false (no info)" and log so the caller can correlate.
            _logger.LogWarning(
                "StatusListCache: no entry available for {Uri} — IsRevoked returning false (no info)",
                statusListUri);
            return false;
        }

        var byteIndex = index / 8;
        var bitOffset = index % 8;
        if (byteIndex >= entry.Bitstring.Length)
        {
            _logger.LogWarning(
                "StatusListCache: index {Index} outside list length {Length} for {Uri}",
                index, entry.Bitstring.Length * 8, statusListUri);
            return false;
        }

        return (entry.Bitstring[byteIndex] & (1 << bitOffset)) != 0;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string statusListUri, CancellationToken ct = default)
    {
        var fresh = await FetchAsync(statusListUri, ct);
        if (fresh is not null)
        {
            _entries[statusListUri] = fresh;
        }
    }

    private async Task<CachedList?> GetOrFetchAsync(string uri, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(uri, out var cached) && cached.ExpiresAt > now)
        {
            return cached;
        }

        var fresh = await FetchAsync(uri, ct);
        if (fresh is not null)
        {
            _entries[uri] = fresh;
            return fresh;
        }

        // Fresh fetch failed — fall back to stale cached entry if any, with a warning.
        if (cached is not null)
        {
            _logger.LogWarning(
                "StatusListCache: fresh fetch failed for {Uri} — serving stale entry (exp {Exp:O})",
                uri, cached.ExpiresAt);
            return cached;
        }

        return null;
    }

    private async Task<CachedList?> FetchAsync(string uri, CancellationToken ct)
    {
        try
        {
            var jwt = await _httpClient.GetStringAsync(uri, ct);
            return ParseJwt(jwt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "StatusListCache: failed to fetch {Uri}", uri);
            return null;
        }
    }

    /// <summary>
    /// Parses a Token Status List 2024 JWT into a <see cref="CachedList"/>. Public
    /// for unit testing — production code goes through <see cref="GetOrFetchAsync"/>.
    /// </summary>
    internal static CachedList ParseJwt(string compactJwt)
    {
        var parts = compactJwt.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Status list JWT must have three parts.");
        }

        var payloadJson = JsonSerializer.Deserialize<JsonElement>(
            Base64Url.DecodeFromChars(parts[1]));

        var statusList = payloadJson.GetProperty("status_list");
        var lstB64 = statusList.GetProperty("lst").GetString()
            ?? throw new FormatException("status_list.lst missing");

        var compressed = Base64Url.DecodeFromChars(lstB64);
        using var ms = new MemoryStream(compressed);
        using var inflater = new ZLibStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);

        var exp = payloadJson.TryGetProperty("exp", out var expEl)
            ? DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64())
            : DateTimeOffset.UtcNow.AddHours(24);

        return new CachedList(output.ToArray(), exp);
    }

    /// <summary>Internal cache entry — bitstring + freshness window.</summary>
    internal sealed record CachedList(byte[] Bitstring, DateTimeOffset ExpiresAt);
}
