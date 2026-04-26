// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// Production <see cref="IStatusListService"/> backed by IndexedDB store
/// <c>statusLists</c> via <c>indexeddb-bridge.js</c>. Caches IETF Token Status
/// List 2024 JWTs by URI; on a cache miss, fetches the signed list over HTTP
/// and stores it. Bit lookup decodes the zlib-compressed bitstring per
/// draft-ietf-oauth-status-list.
///
/// Issuer-signature verification against the org status-signing key is deferred
/// to a follow-up wave (full DID resolver wiring); v1 trusts the served list
/// when fetched over TLS. Verifiers do their own signature check independently
/// — this service is a wallet-side pre-flight to refuse known-revoked
/// credentials before generating a presentation, not the authoritative check.
/// </summary>
public sealed class IndexedDbStatusListService : IStatusListService
{
    private const string StoreName = "statusLists";

    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    /// <summary>Initialises a new instance.</summary>
    public IndexedDbStatusListService(IJSRuntime js, HttpClient http, TimeProvider clock)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string uri, int index, CancellationToken ct = default)
    {
        var row = await _js.InvokeAsync<StatusListRow?>("SorchaIndexedDb.get", ct, StoreName, uri);
        var nowEpoch = _clock.GetUtcNow().ToUnixTimeSeconds();

        if (row is null || row.Exp <= nowEpoch)
        {
            row = await FetchAsync(uri, ct);
            if (row is null) return false; // fail-open at wallet pre-flight; verifier still checks
            await _js.InvokeVoidAsync("SorchaIndexedDb.put", ct, StoreName, row);
        }

        return BitstringHasIndexSet(row.Bitstring, index);
    }

    private async Task<StatusListRow?> FetchAsync(string uri, CancellationToken ct)
    {
        try
        {
            var jwt = await _http.GetStringAsync(uri, ct);
            return ParseStatusListJwt(uri, jwt);
        }
        catch
        {
            return null;
        }
    }

    internal static StatusListRow? ParseStatusListJwt(string uri, string compactJwt)
    {
        var parts = compactJwt.Split('.');
        if (parts.Length != 3) return null;
        var payloadBytes = Base64Url.DecodeFromChars(parts[1]);
        using var doc = JsonDocument.Parse(payloadBytes);
        var root = doc.RootElement;
        var iat = root.TryGetProperty("iat", out var iatEl) ? iatEl.GetInt64() : 0;
        var exp = root.TryGetProperty("exp", out var expEl) ? expEl.GetInt64() : long.MaxValue;
        if (!root.TryGetProperty("status_list", out var sl)) return null;
        var lst = sl.GetProperty("lst").GetString() ?? string.Empty;
        var compressed = Base64Url.DecodeFromChars(lst);
        var bits = ZlibInflate(compressed);
        return new StatusListRow(uri, bits, iat, exp, compactJwt, DateTimeOffset.UtcNow);
    }

    private static byte[] ZlibInflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var deflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    internal static bool BitstringHasIndexSet(byte[] bitstring, int index)
    {
        if (index < 0) return false;
        var byteIndex = index >> 3;
        if (byteIndex >= bitstring.Length) return false;
        var bitInByte = index & 0b111;
        return (bitstring[byteIndex] & (1 << bitInByte)) != 0;
    }

    internal sealed record StatusListRow(
        string Uri,
        byte[] Bitstring,
        long Iat,
        long Exp,
        string SignedJwt,
        DateTimeOffset FetchedAt);
}
