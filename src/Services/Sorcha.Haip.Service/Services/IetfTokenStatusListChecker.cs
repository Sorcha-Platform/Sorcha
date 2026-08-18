// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CredentialStatusValue = Sorcha.Blueprint.Engine.Credentials.CredentialStatusValue;
using EngineCredentials = Sorcha.Blueprint.Engine.Credentials;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 095 US4 — client-side verifier for the IETF Token Status List
/// endpoint. Fetches the signed envelope JWT, verifies its signature against
/// the embedded JWK header, decompresses the zlib-packed <c>lst</c> bitstring,
/// and reads the bit at the requested index.
/// </summary>
/// <remarks>
/// The envelope is signed by the list's issuer. The public JWK is embedded in
/// the JWT header during the pre-x5c dev phase (matching
/// <see cref="IetfTokenStatusListSerializer"/>); once spec 096 ships, a real
/// <c>x5c</c> chain will replace the embedded JWK and this checker should be
/// extended to walk the chain. No signature = refuse to trust the bitstring.
/// </remarks>
public sealed class IetfTokenStatusListChecker : EngineCredentials.IStatusListChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IetfTokenStatusListChecker> _logger;

    public IetfTokenStatusListChecker(HttpClient httpClient, ILogger<IetfTokenStatusListChecker> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Feature 135 (T023) — unified status seam. Adapts the IETF Token Status List check to the
    /// engine's <see cref="EngineCredentials.IStatusListChecker"/> so both verification paths read
    /// status identically.
    /// </summary>
    /// <remarks>
    /// Feature 192 removed the translation step that used to live here. There were two
    /// identically-named <c>StatusListBit</c> enums — one local, one in the engine — and this
    /// method mapped between them; both were tri-states, so the SUSPENDED value this checker had
    /// just decoded had nowhere to go. The local enum is gone and the read path returns
    /// <see cref="CredentialStatusValue"/> throughout, so there is nothing left to translate.
    /// </remarks>
    public Task<CredentialStatusValue> CheckAsync(
        EngineCredentials.StatusReference statusRef, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statusRef);
        if (string.IsNullOrWhiteSpace(statusRef.Uri))
            return Task.FromResult(CredentialStatusValue.Unresolved);

        return CheckBitAsync(statusRef.Uri, statusRef.Index, cancellationToken);
    }

    /// <summary>
    /// Reads the status of the entry at <paramref name="idx"/>, or
    /// <see cref="CredentialStatusValue.Unresolved"/> when the envelope cannot be fetched,
    /// verified, or decoded. The verifier treats Unresolved as a non-fatal signal — the caller
    /// decides whether to fail-open or fail-closed.
    /// </summary>
    public async Task<CredentialStatusValue> CheckBitAsync(
        string uri, int idx, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return CredentialStatusValue.Unresolved;
        if (idx < 0)
            return CredentialStatusValue.Unresolved;

        string jwt;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/statuslist+jwt"));
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseContentRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "IETF status list fetch failed: {StatusCode} {ReasonPhrase} for {Uri}",
                    (int)response.StatusCode, response.ReasonPhrase, uri);
                return CredentialStatusValue.Unresolved;
            }
            jwt = (await response.Content.ReadAsStringAsync(ct)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "IETF status list fetch errored for {Uri}", uri);
            return CredentialStatusValue.Unresolved;
        }

        return ParseAndReadBit(jwt, idx);
    }

    /// <summary>
    /// Pure-function variant: decodes a signed IETF Token Status List JWT,
    /// verifies the signature against the embedded <c>jwk</c> header, and reads
    /// the entry at <paramref name="idx"/>. Returns <see cref="CredentialStatusValue.Unresolved"/>
    /// on any parse, signature, or index failure. Extracted for unit testing
    /// without an <see cref="HttpClient"/>.
    /// </summary>
    internal static CredentialStatusValue ParseAndReadBit(string jwt, int idx)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return CredentialStatusValue.Unresolved;

        JsonElement header;
        JsonElement payload;
        byte[] signature;
        try
        {
            header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
            payload = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[1]));
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch
        {
            return CredentialStatusValue.Unresolved;
        }

        // Sanity-check typ — must be "statuslist+jwt".
        if (!header.TryGetProperty("typ", out var typEl)
            || !string.Equals(typEl.GetString(), "statuslist+jwt", StringComparison.Ordinal))
        {
            return CredentialStatusValue.Unresolved;
        }

        if (!header.TryGetProperty("alg", out var algEl))
            return CredentialStatusValue.Unresolved;
        var alg = algEl.GetString();
        if (string.IsNullOrEmpty(alg)) return CredentialStatusValue.Unresolved;

        // Verify the envelope signature using the embedded JWK header. Pre-x5c
        // dev posture — once spec 096 lands this should walk the x5c chain and
        // validate against the trust anchor.
        if (!header.TryGetProperty("jwk", out var jwkEl))
            return CredentialStatusValue.Unresolved;

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        if (!VerifySignature(signingInput, signature, jwkEl, alg))
            return CredentialStatusValue.Unresolved;

        // Decompress the zlib-packed bitstring and read the bit.
        if (!payload.TryGetProperty("status_list", out var statusList))
            return CredentialStatusValue.Unresolved;
        if (!statusList.TryGetProperty("lst", out var lstEl))
            return CredentialStatusValue.Unresolved;
        var bits = statusList.TryGetProperty("bits", out var bitsEl) && bitsEl.TryGetInt32(out var b)
            ? b : 1;

        byte[] raw;
        try
        {
            var compressed = Base64Url.DecodeFromChars(lstEl.GetString()!);
            raw = ZLibDecompress(compressed);
        }
        catch
        {
            return CredentialStatusValue.Unresolved;
        }

        // Multi-bit lists pack `bits` bits per entry. 1-bit lists are the common case.
        return ReadBit(raw, idx, bits);
    }

    private static bool VerifySignature(byte[] signingInput, byte[] signature, JsonElement jwk, string alg)
    {
        try
        {
            var normalisedAlg = alg.ToUpperInvariant();
            if (normalisedAlg is "ES256" or "P-256" or "P256"
                && jwk.TryGetProperty("kty", out var kty) && kty.GetString() == "EC"
                && jwk.TryGetProperty("x", out var xEl) && jwk.TryGetProperty("y", out var yEl))
            {
                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = Base64Url.DecodeFromChars(xEl.GetString()!),
                        Y = Base64Url.DecodeFromChars(yEl.GetString()!),
                    },
                });
                // IetfTokenStatusListSerializer signs with raw SignData → DER output
                // by default. Accept both IEEE P1363 concatenated and DER forms.
                return ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256)
                    || ecdsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }

            if (normalisedAlg is "EDDSA" or "ED25519"
                && jwk.TryGetProperty("kty", out var kty2) && kty2.GetString() == "OKP"
                && jwk.TryGetProperty("x", out var okpXEl))
            {
                var publicKey = Base64Url.DecodeFromChars(okpXEl.GetString()!);
                return Sodium.PublicKeyAuth.VerifyDetached(signature, signingInput, publicKey);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ZLibDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Reads the STATUS VALUE of entry <paramref name="idx"/> from a bitstring where each entry
    /// takes <paramref name="bitsPerEntry"/> bits, MSB-first both across and within bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Feature 192. This method used to return "set" if ANY bit in the entry was set, which made
    /// <c>0x02</c> SUSPENDED and <c>0x01</c> INVALID indistinguishable BY CONSTRUCTION — the read
    /// side threw away the distinction #1492 had just gone to the trouble of encoding correctly on
    /// the write side. It now accumulates the entry's bits into its value and reports that.
    /// </para>
    /// <para>
    /// The accumulation order mirrors <c>IetfStatusListPacker.PackTwoBit</c>, which writes the
    /// high bit at <c>2i</c> and the low bit at <c>2i+1</c>, so the two round-trip.
    /// </para>
    /// <para>
    /// A value the spec reserves for application-specific use (<c>0x03</c> and above) is reported
    /// as <see cref="CredentialStatusValue.Unresolved"/>, NOT as a status. We genuinely cannot
    /// interpret it, and "I could not tell" is the honest answer — the caller's fail-closed policy
    /// then decides, which for the default FailClosed still refuses.
    /// </para>
    /// </remarks>
    internal static CredentialStatusValue ReadBit(byte[] raw, int idx, int bitsPerEntry)
    {
        // The spec permits exactly these widths. Anything else means we have misread the envelope,
        // and guessing a layout would invent a status for whichever entry we happened to land on.
        if (bitsPerEntry is not (1 or 2 or 4 or 8))
            return CredentialStatusValue.Unresolved;

        var startBit = (long)idx * bitsPerEntry;
        var endBit = startBit + bitsPerEntry;
        if (idx < 0 || endBit > (long)raw.Length * 8)
            return CredentialStatusValue.Unresolved;

        var value = 0;
        for (var bit = startBit; bit < endBit; bit++)
        {
            var byteIdx = (int)(bit / 8);
            var bitIdx = (int)(bit % 8);
            // IETF & W3C use MSB-first bit ordering within a byte.
            var isSet = (raw[byteIdx] & (1 << (7 - bitIdx))) != 0;
            value = (value << 1) | (isSet ? 1 : 0);
        }

        return value switch
        {
            IetfStatusValue.Valid => CredentialStatusValue.Valid,
            IetfStatusValue.Invalid => CredentialStatusValue.Invalid,
            IetfStatusValue.Suspended => CredentialStatusValue.Suspended,
            _ => CredentialStatusValue.Unresolved
        };
    }

    /// <summary>
    /// The IETF Token Status List status values this verifier understands. Mirrors the write-side
    /// constants on <c>IetfStatusListPacker</c>, which lives in the Blueprint Service — the two
    /// services do not share an assembly, so the values are pinned by
    /// <c>IetfStatusValueReadTests</c> on each side rather than by a shared type.
    /// </summary>
    private static class IetfStatusValue
    {
        public const int Valid = 0x00;
        public const int Invalid = 0x01;
        public const int Suspended = 0x02;
    }
}
