// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Decides whether an IETF Token Status List (RFC 9972 / draft-ietf-oauth-status-list) may be
/// believed — the ONE implementation, shared by <see cref="StatusListCache"/> and the HAIP verifier
/// (#1759, #1768). Two copies of this rule is how HAIP came to trust a key the list carried itself.
/// </summary>
/// <remarks>
/// <para>A list is believed only if ALL hold:</para>
/// <list type="number">
///   <item><c>typ</c> is <c>statuslist+jwt</c> (§5.1 MUST);</item>
///   <item><c>iss</c> equals the issuer the caller expects — the credential's own issuer, or the signer a
///   delegation names — so a genuine list from somebody else cannot stand in;</item>
///   <item><c>sub</c> equals the <c>status_list.uri</c> the credential carries (§5.1 / §8.3 MUST), so a
///   genuine list for OTHER credentials cannot stand in;</item>
///   <item>the key is resolved from the issuer's DID by the JWS <c>kid</c> (§11.3) — never taken from the
///   token itself — and the signature verifies against it (ES256, ES256K or EdDSA);</item>
///   <item><c>exp</c> is present and not past, within the clock skew;</item>
///   <item><c>status_list.bits</c> is 1, 2, 4 or 8 and <c>lst</c> decompresses.</item>
/// </list>
/// <para>Any failure is a rejection with a reason; callers fail closed.</para>
/// </remarks>
public sealed class StatusListTokenVerifier
{
    /// <summary>The JWT <c>typ</c> the IETF Token Status List requires.</summary>
    public const string MediaType = "statuslist+jwt";

    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(60);

    private readonly IIssuerKeyResolver _issuerKeys;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly TimeSpan _clockSkew;

    /// <summary>Initialises a new instance of the <see cref="StatusListTokenVerifier"/> class.</summary>
    public StatusListTokenVerifier(
        IIssuerKeyResolver issuerKeys,
        TimeProvider clock,
        ILogger logger,
        TimeSpan? clockSkew = null)
    {
        _issuerKeys = issuerKeys ?? throw new ArgumentNullException(nameof(issuerKeys));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clockSkew = clockSkew ?? DefaultClockSkew;
    }

    /// <summary>Verifies a status list token fetched from <paramref name="expectedUri"/>.</summary>
    /// <param name="compactJwt">The token as served.</param>
    /// <param name="expectedUri">The <c>status_list.uri</c> the credential carries.</param>
    /// <param name="expectedIssuer">The DID whose signature the list must carry.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<StatusListTokenResult> VerifyAsync(
        string compactJwt, string expectedUri, string expectedIssuer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedIssuer);

        JsonElement header, payload;
        try
        {
            var parts = (compactJwt ?? string.Empty).Trim().Split('.');
            if (parts.Length != 3) return Reject(StatusListTokenRejection.Malformed, expectedUri);
            header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
            payload = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
            if (header.ValueKind != JsonValueKind.Object || payload.ValueKind != JsonValueKind.Object)
                return Reject(StatusListTokenRejection.Malformed, expectedUri);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return Reject(StatusListTokenRejection.Malformed, expectedUri);
        }

        if (!string.Equals(ReadString(header, "typ"), MediaType, StringComparison.Ordinal))
            return Reject(StatusListTokenRejection.WrongType, expectedUri);

        if (!string.Equals(ReadString(payload, "iss"), expectedIssuer, StringComparison.Ordinal))
            return Reject(StatusListTokenRejection.IssuerMismatch, expectedUri,
                $"iss '{ReadString(payload, "iss")}' is not '{expectedIssuer}'");

        if (!string.Equals(ReadString(payload, "sub"), expectedUri, StringComparison.Ordinal))
            return Reject(StatusListTokenRejection.SubjectMismatch, expectedUri,
                $"sub '{ReadString(payload, "sub")}' is not the credential's status_list.uri");

        // The key comes from the issuer's DID, selected by kid — never from the token itself.
        var jwk = await _issuerKeys.ResolveAsync(expectedIssuer, ReadString(header, "kid"), ct);
        if (jwk is null)
            return Reject(StatusListTokenRejection.KeyUnresolved, expectedUri);

        if (!VerifiablePresentationValidator.VerifyJwsSignature(compactJwt!.Trim(), jwk.Value, out _))
            return Reject(StatusListTokenRejection.SignatureInvalid, expectedUri);

        if (!payload.TryGetProperty("exp", out var expEl) || expEl.ValueKind != JsonValueKind.Number)
            return Reject(StatusListTokenRejection.Expired, expectedUri, "no exp");
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
        if (_clock.GetUtcNow() > expiresAt + _clockSkew)
            return Reject(StatusListTokenRejection.Expired, expectedUri, $"expired at {expiresAt:O}");

        if (!payload.TryGetProperty("status_list", out var statusList) || statusList.ValueKind != JsonValueKind.Object)
            return Reject(StatusListTokenRejection.Malformed, expectedUri, "no status_list");

        // A list that omits bits predates Sorcha declaring it and is 1-bit (#1499).
        var bits = statusList.TryGetProperty("bits", out var bitsEl) && bitsEl.TryGetInt32(out var b) ? b : 1;
        if (bits is not (1 or 2 or 4 or 8))
            return Reject(StatusListTokenRejection.UnsupportedBits, expectedUri, $"bits {bits}");

        byte[] entries;
        try
        {
            var lst = ReadString(statusList, "lst") ?? throw new FormatException("no lst");
            using var input = new MemoryStream(Base64Url.DecodeFromChars(lst));
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            entries = output.ToArray();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return Reject(StatusListTokenRejection.Malformed, expectedUri, "lst does not decompress");
        }

        return new StatusListTokenResult(null, entries, bits, expiresAt);
    }

    /// <summary>
    /// Reads entry <paramref name="index"/>'s VALUE (IETF layout: least-significant bit first, §4.1),
    /// or null when the index lies outside the list. The opposite bit order to W3C Bitstring Status
    /// List — the two specs genuinely disagree (#1761).
    /// </summary>
    public static int? ReadEntry(byte[] entries, int bits, int index)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (bits is not (1 or 2 or 4 or 8) || index < 0) return null;

        var startBit = (long)index * bits;
        if (startBit + bits > (long)entries.Length * 8) return null;

        // Permitted widths divide 8, so an entry never straddles a byte.
        return (entries[(int)(startBit / 8)] >> (int)(startBit % 8)) & ((1 << bits) - 1);
    }

    private StatusListTokenResult Reject(StatusListTokenRejection reason, string uri, string? detail = null)
    {
        _logger.LogWarning("Status list {Uri} rejected: {Reason}{Detail} — failing closed",
            uri, reason, detail is null ? string.Empty : $" ({detail})");
        return new StatusListTokenResult(reason, [], 0, default);
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}

/// <summary>Why a status list token was not believed.</summary>
public enum StatusListTokenRejection
{
    /// <summary>Not a parseable JWT, or its status_list is unreadable.</summary>
    Malformed,
    /// <summary><c>typ</c> is not <c>statuslist+jwt</c>.</summary>
    WrongType,
    /// <summary><c>iss</c> is not the issuer the caller expects.</summary>
    IssuerMismatch,
    /// <summary><c>sub</c> is not the credential's <c>status_list.uri</c>.</summary>
    SubjectMismatch,
    /// <summary>No key could be resolved from the issuer's DID for this <c>kid</c>.</summary>
    KeyUnresolved,
    /// <summary>The signature does not verify against the resolved key.</summary>
    SignatureInvalid,
    /// <summary><c>exp</c> is missing or past.</summary>
    Expired,
    /// <summary><c>bits</c> is not 1, 2, 4 or 8.</summary>
    UnsupportedBits,
}

/// <summary>The outcome of verifying a status list token.</summary>
/// <param name="Rejection">Null when the list is verified.</param>
/// <param name="Entries">The decompressed entry bytes (verified lists only).</param>
/// <param name="Bits">Bits per entry (verified lists only).</param>
/// <param name="ExpiresAt">The token's <c>exp</c> (verified lists only).</param>
public sealed record StatusListTokenResult(
    StatusListTokenRejection? Rejection, byte[] Entries, int Bits, DateTimeOffset ExpiresAt)
{
    /// <summary>Whether the list may be believed.</summary>
    public bool IsVerified => Rejection is null;
}
