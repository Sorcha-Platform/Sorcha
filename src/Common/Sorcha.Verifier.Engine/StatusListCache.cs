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
    private readonly StatusListTokenVerifier _verifier;
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
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _clockSkew = clockSkew ?? DefaultClockSkew;
        // #1759 — the one implementation of "may this list be believed", shared with HAIP.
        _verifier = new StatusListTokenVerifier(
            issuerKeys ?? throw new ArgumentNullException(nameof(issuerKeys)), clock, logger, _clockSkew);
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

        var value = StatusListTokenVerifier.ReadEntry(entry.Bitstring, bits, index);
        if (value is null)
        {
            // Index outside the list. The list is authentic but says nothing about this credential —
            // an out-of-range index is itself suspicious. Fail closed.
            _logger.LogWarning(
                "StatusListCache: index {Index} outside list length {Length} for {Uri} — failing closed",
                index, entry.Bitstring.Length * 8 / bits, statusListUri);
            return StatusListVerdict.Unverifiable;
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
        var result = await _verifier.VerifyAsync(compactJwt, uri, expectedIssuer, ct);
        if (!result.IsVerified)
        {
            _metrics?.StatusListRejected(result.Rejection switch
            {
                StatusListTokenRejection.IssuerMismatch or StatusListTokenRejection.SubjectMismatch => "issuer",
                StatusListTokenRejection.KeyUnresolved => "unresolved",
                StatusListTokenRejection.Expired => "expired",
                _ => "signature",
            });
            return null;
        }

        return new CachedList(result.Entries, result.ExpiresAt, expectedIssuer, result.Bits);
    }

    private bool ClockSkewExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now > expiresAt + _clockSkew;

    /// <summary>Internal cache entry — only ever holds a verified list. Issuer recorded for pinning re-check.</summary>
    internal sealed record CachedList(byte[] Bitstring, DateTimeOffset ExpiresAt, string Issuer, int Bits);
}
