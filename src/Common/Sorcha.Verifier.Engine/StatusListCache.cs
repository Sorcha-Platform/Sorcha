// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Default <see cref="IStatusListCache"/>. In-memory cache keyed by status list URI; entries hold the
/// decoded bitstring and the JWT's <c>exp</c>. Concurrent — multiple presentations against the same
/// list share a single decode.
/// </summary>
/// <remarks>
/// <para>
/// Feature 138 US1 hardening — the cache no longer trusts a fetched list on the strength of the
/// transport. Before any revocation bit is read it:
/// </para>
/// <list type="number">
///   <item>resolves the issuing org's public key from sealed register state via <see cref="IIssuerKeyResolver"/>;</item>
///   <item>verifies the list JWT's signature against that key;</item>
///   <item>pins the list's <c>iss</c> to the credential's expected org DID;</item>
///   <item>enforces freshness against the list's own <c>exp</c> within a bounded clock skew.</item>
/// </list>
/// <para>
/// Every failure path returns <see cref="StatusListVerdict.Unverifiable"/> (fail closed) and increments
/// <c>sorcha_statuslist_rejected_total</c>. Only a fully-verified list is cached; a fetch failure never
/// serves a stale copy. The host owns any health-posture surface
/// (<c>ISecurityPostureSignal</c> in <c>Sorcha.ServiceDefaults</c>) — the engine stays dependency-light
/// and signals through the metric and logs.
/// </para>
/// <para>
/// Only ES256 (P-256) list signatures are verifiable in-engine, matching the rest of the engine's
/// ES256-only JWS posture (the citizen wallet's default classical algorithm). A list signed with any
/// other algorithm is treated as <see cref="StatusListVerdict.Unverifiable"/> — fail closed, never open.
/// </para>
/// <para>
/// Issue #1499. The bit read honours the envelope's declared <c>status_list.bits</c> width instead of
/// assuming 1-bit entries — a hardcoded 1-bit stride against a <c>bits=2</c> (or wider) list reads
/// entry <c>N/2</c> for a request at index <c>N</c>, fabricating a status for a credential nobody
/// touched (the same failure mode #1492 fixed on the sibling IETF rail's write side). The bit order
/// itself is <b>LSB-first</b> — entry <c>index</c>'s value occupies bits
/// <c>[index*bits, index*bits+bits)</c> counting from bit 0 (least significant) of byte 0 upward, per
/// draft-ietf-oauth-status-list §4.1 ("packed into bytes from the least significant bit (0) to the
/// most significant bit (7)"). That is the OPPOSITE convention to W3C Bitstring Status List (MSB-first,
/// <c>BitstringStatusList.GetBit/SetBit</c>) — the two specs disagree on this, deliberately, and
/// conflating them is the mistake to guard against here. Verified directly
/// against the IETF draft's own worked examples, which zlib-decompress to the raw bytes
/// <c>0xb9 0xa3</c> (bits=1, 16 entries) and <c>0xc9 0x44 0xf9</c> (bits=2, 12 entries) — both of
/// which only reconstruct the spec's stated per-index status values under LSB-first packing, not
/// MSB-first. <c>StatusListCacheIetfConformanceTests</c> pins both vectors verbatim.
/// </para>
/// </remarks>
public sealed class StatusListCache : IStatusListCache
{
    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly IIssuerKeyResolver _issuerKeys;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _clockSkew;
    private readonly ILogger<StatusListCache> _logger;
    private readonly FederationVerifierMetrics? _metrics;

    private readonly ConcurrentDictionary<string, CachedList> _entries = new();

    /// <summary>Initialises a new instance of the <see cref="StatusListCache"/> class.</summary>
    public StatusListCache(
        HttpClient httpClient,
        IIssuerKeyResolver issuerKeys,
        TimeProvider clock,
        ILogger<StatusListCache> logger,
        FederationVerifierMetrics? metrics = null,
        TimeSpan? clockSkew = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _issuerKeys = issuerKeys ?? throw new ArgumentNullException(nameof(issuerKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _clockSkew = clockSkew ?? DefaultClockSkew;
    }

    /// <inheritdoc />
    public async Task<StatusListVerdict> CheckAsync(
        string statusListUri, int index, string expectedIssuer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusListUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIssuer);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        var entry = await GetOrFetchVerifiedAsync(statusListUri, expectedIssuer, ct);
        if (entry is null)
        {
            // Could not fetch or could not verify — fail closed. The specific reason was already
            // logged + counted inside the fetch/verify path.
            return StatusListVerdict.Unverifiable;
        }

        // #1499: honour the envelope's declared entry width instead of assuming 1-bit entries. The
        // spec permits exactly these widths (draft-ietf-oauth-status-list §4.1); anything else means
        // we have misread the envelope, and guessing a layout would invent a status for whichever
        // entry we happened to land on — the same reasoning IetfTokenStatusListChecker.ReadBit uses
        // on the sibling IETF rail.
        var bits = entry.Bits;
        if (bits is not (1 or 2 or 4 or 8))
        {
            _logger.LogWarning(
                "StatusListCache: unsupported status_list.bits {Bits} for {Uri} — failing closed",
                bits, statusListUri);
            return StatusListVerdict.Unverifiable;
        }

        var startBit = (long)index * bits;
        var endBit = startBit + bits;
        if (endBit > (long)entry.Bitstring.Length * 8)
        {
            // Index outside the list. The list is authentic but says nothing about this credential —
            // an out-of-range index is itself suspicious. Fail closed.
            _logger.LogWarning(
                "StatusListCache: index {Index} outside list length {Length} for {Uri} — failing closed",
                index, entry.Bitstring.Length * 8 / bits, statusListUri);
            return StatusListVerdict.Unverifiable;
        }

        // LSB-first per draft-ietf-oauth-status-list §4.1: entry `index`'s value occupies bits
        // [index*bits, index*bits+bits) counting from bit 0 (least significant) of byte 0 upward, and
        // within that range bit `index*bits + b` carries value-bit `b` (weight 2^b). This is the
        // OPPOSITE convention to W3C Bitstring Status List's MSB-first packing — see the class remarks.
        var value = 0;
        for (var b = 0; b < bits; b++)
        {
            var globalBit = startBit + b;
            var byteIndex = (int)(globalBit / 8);
            var bitOffset = (int)(globalBit % 8);
            var isSet = (entry.Bitstring[byteIndex] & (1 << bitOffset)) != 0;
            if (isSet) value |= 1 << b;
        }

        // StatusListVerdict has no SUSPENDED state (that is #1498's open follow-up, deliberately out
        // of scope here) — any non-zero entry value is reported as Revoked, matching the pre-#1499
        // binary semantics this rail has only ever actually published (1-bit, revoked-or-not).
        return value == 0 ? StatusListVerdict.Active : StatusListVerdict.Revoked;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(string statusListUri, string expectedIssuer, CancellationToken ct = default)
    {
        var fresh = await FetchAndVerifyAsync(statusListUri, expectedIssuer, ct);
        if (fresh is not null)
        {
            _entries[statusListUri] = fresh;
        }
    }

    private async Task<CachedList?> GetOrFetchVerifiedAsync(string uri, string expectedIssuer, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        if (_entries.TryGetValue(uri, out var cached)
            && !ClockSkewExpired(cached.ExpiresAt, now)
            && string.Equals(cached.Issuer, expectedIssuer, StringComparison.Ordinal))
        {
            return cached;
        }

        // Fail closed: a fresh fetch/verify failure does NOT fall back to a stale cached entry.
        var fresh = await FetchAndVerifyAsync(uri, expectedIssuer, ct);
        if (fresh is not null)
        {
            _entries[uri] = fresh;
            return fresh;
        }

        return null;
    }

    private async Task<CachedList?> FetchAndVerifyAsync(string uri, string expectedIssuer, CancellationToken ct)
    {
        string jwt;
        try
        {
            jwt = await _httpClient.GetStringAsync(uri, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "StatusListCache: fetch failed for {Uri} — failing closed", uri);
            _metrics?.StatusListRejected("fetch");
            return null;
        }

        return await VerifyAsync(jwt, uri, expectedIssuer, ct);
    }

    private async Task<CachedList?> VerifyAsync(string compactJwt, string uri, string expectedIssuer, CancellationToken ct)
    {
        ParsedList parsed;
        try
        {
            parsed = ParseJwt(compactJwt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StatusListCache: malformed status list for {Uri} — failing closed", uri);
            _metrics?.StatusListRejected("signature");
            return null;
        }

        // ── Issuer pinning (FR-002) ───────────────────────────────────────────────
        if (string.IsNullOrEmpty(parsed.Issuer)
            || !string.Equals(parsed.Issuer, expectedIssuer, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "StatusListCache: issuer mismatch for {Uri} — list iss '{Actual}' ≠ expected '{Expected}'",
                uri, parsed.Issuer, expectedIssuer);
            _metrics?.StatusListRejected("issuer");
            return null;
        }

        // ── Resolve the issuing org's key from sealed state (FR-001) ──────────────
        var jwk = await _issuerKeys.ResolveAsync(expectedIssuer, parsed.Kid, ct);
        if (jwk is null)
        {
            _logger.LogWarning(
                "StatusListCache: no key resolved for issuer '{Issuer}' (kid '{Kid}') for {Uri} — failing closed",
                expectedIssuer, parsed.Kid, uri);
            _metrics?.StatusListRejected("unresolved");
            return null;
        }

        // ── Signature verification (FR-001) ───────────────────────────────────────
        if (!VerifyListSignature(parsed, jwk.Value))
        {
            _logger.LogWarning(
                "StatusListCache: signature verification failed for {Uri} against issuer '{Issuer}' key",
                uri, expectedIssuer);
            _metrics?.StatusListRejected("signature");
            return null;
        }

        // ── Freshness (FR-004) — list MUST carry exp and MUST be fresh within skew ─
        if (parsed.ExpiresAt is null)
        {
            _logger.LogWarning("StatusListCache: status list for {Uri} has no exp — failing closed", uri);
            _metrics?.StatusListRejected("expired");
            return null;
        }
        if (ClockSkewExpired(parsed.ExpiresAt.Value, _clock.GetUtcNow()))
        {
            _logger.LogWarning(
                "StatusListCache: status list for {Uri} expired at {Exp:O} — failing closed",
                uri, parsed.ExpiresAt.Value);
            _metrics?.StatusListRejected("expired");
            return null;
        }

        return new CachedList(parsed.Bitstring, parsed.ExpiresAt.Value, expectedIssuer, parsed.Bits);
    }

    private bool ClockSkewExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now > expiresAt + _clockSkew;

    /// <summary>
    /// Verifies a status-list JWS signature against a public JWK. ES256 (P-256) only — any other
    /// algorithm fails closed, consistent with the engine's ES256-only JWS posture.
    /// </summary>
    private static bool VerifyListSignature(ParsedList parsed, JsonElement jwk)
    {
        try
        {
            if (!string.Equals(parsed.Alg, "ES256", StringComparison.Ordinal)) return false;
            if (!jwk.TryGetProperty("x", out var xEl) || !jwk.TryGetProperty("y", out var yEl)) return false;
            var x = xEl.GetString();
            var y = yEl.GetString();
            if (x is null || y is null) return false;

            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = Base64Url.DecodeFromChars(x),
                    Y = Base64Url.DecodeFromChars(y),
                },
            });

            // Citizen wallet signs with raw ECDSA → IEEE P1363 fixed-field concatenation; accept DER too.
            return ecdsa.VerifyData(parsed.SigningInput, parsed.Signature, HashAlgorithmName.SHA256,
                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                || ecdsa.VerifyData(parsed.SigningInput, parsed.Signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a Token Status List 2024 JWT into a <see cref="ParsedList"/> — bitstring, claims, and the
    /// signing input + signature needed to authenticate it. Public for unit testing — production code
    /// goes through <see cref="CheckAsync"/>. Does NOT verify the signature (the cache does that).
    /// </summary>
    internal static ParsedList ParseJwt(string compactJwt)
    {
        var parts = compactJwt.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("Status list JWT must have three parts.");
        }

        var headerJson = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
        var alg = headerJson.TryGetProperty("alg", out var algEl) && algEl.ValueKind == JsonValueKind.String
            ? algEl.GetString() ?? string.Empty
            : string.Empty;
        var kid = headerJson.TryGetProperty("kid", out var kidEl) && kidEl.ValueKind == JsonValueKind.String
            ? kidEl.GetString()
            : null;

        var payloadJson = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));

        var statusList = payloadJson.GetProperty("status_list");
        var lstB64 = statusList.GetProperty("lst").GetString()
            ?? throw new FormatException("status_list.lst missing");

        var compressed = Base64Url.DecodeFromChars(lstB64);
        using var ms = new MemoryStream(compressed);
        using var inflater = new ZLibStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);

        // #1499: honour the declared entry width instead of assuming 1-bit entries. Every list this
        // rail has published so far omits `bits` (implicitly 1) or sets it to 1 explicitly, so the
        // default preserves every already-published list's meaning exactly.
        var bits = statusList.TryGetProperty("bits", out var bitsEl) && bitsEl.TryGetInt32(out var b)
            ? b : 1;

        // No +24h default — a list with no exp is rejected by the freshness gate (FR-004).
        DateTimeOffset? exp = payloadJson.TryGetProperty("exp", out var expEl) && expEl.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64())
            : null;

        var issuer = payloadJson.TryGetProperty("iss", out var issEl) && issEl.ValueKind == JsonValueKind.String
            ? issEl.GetString()
            : null;

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64Url.DecodeFromChars(parts[2]);

        return new ParsedList(output.ToArray(), exp, issuer, alg, kid, signingInput, signature, bits);
    }

    /// <summary>Parsed-but-unverified status list: bitstring, claims, and the material to authenticate it.</summary>
    internal sealed record ParsedList(
        byte[] Bitstring,
        DateTimeOffset? ExpiresAt,
        string? Issuer,
        string Alg,
        string? Kid,
        byte[] SigningInput,
        byte[] Signature,
        int Bits);

    /// <summary>Internal cache entry — only ever holds a verified list. Issuer recorded for pinning re-check.</summary>
    internal sealed record CachedList(byte[] Bitstring, DateTimeOffset ExpiresAt, string Issuer, int Bits);
}
