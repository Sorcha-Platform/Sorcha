// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
public sealed class IetfTokenStatusListChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IetfTokenStatusListChecker> _logger;

    public IetfTokenStatusListChecker(HttpClient httpClient, ILogger<IetfTokenStatusListChecker> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns <see cref="StatusListBit.NotSet"/> when the bit at
    /// <paramref name="idx"/> is 0 (active), <see cref="StatusListBit.Set"/>
    /// when it is 1 (revoked/suspended), or <see cref="StatusListBit.Unknown"/>
    /// when the envelope cannot be fetched, verified, or decoded. The verifier
    /// treats <c>Unknown</c> as a non-fatal status signal — the caller decides
    /// whether to fail-open or fail-closed.
    /// </summary>
    public async Task<StatusListBit> CheckBitAsync(
        string uri, int idx, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return StatusListBit.Unknown;
        if (idx < 0)
            return StatusListBit.Unknown;

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
                return StatusListBit.Unknown;
            }
            jwt = (await response.Content.ReadAsStringAsync(ct)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "IETF status list fetch errored for {Uri}", uri);
            return StatusListBit.Unknown;
        }

        return ParseAndReadBit(jwt, idx);
    }

    /// <summary>
    /// Pure-function variant: decodes a signed IETF Token Status List JWT,
    /// verifies the signature against the embedded <c>jwk</c> header, and reads
    /// the bit at <paramref name="idx"/>. Returns <see cref="StatusListBit.Unknown"/>
    /// on any parse, signature, or index failure. Extracted for unit testing
    /// without an <see cref="HttpClient"/>.
    /// </summary>
    internal static StatusListBit ParseAndReadBit(string jwt, int idx)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return StatusListBit.Unknown;

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
            return StatusListBit.Unknown;
        }

        // Sanity-check typ — must be "statuslist+jwt".
        if (!header.TryGetProperty("typ", out var typEl)
            || !string.Equals(typEl.GetString(), "statuslist+jwt", StringComparison.Ordinal))
        {
            return StatusListBit.Unknown;
        }

        if (!header.TryGetProperty("alg", out var algEl))
            return StatusListBit.Unknown;
        var alg = algEl.GetString();
        if (string.IsNullOrEmpty(alg)) return StatusListBit.Unknown;

        // Verify the envelope signature using the embedded JWK header. Pre-x5c
        // dev posture — once spec 096 lands this should walk the x5c chain and
        // validate against the trust anchor.
        if (!header.TryGetProperty("jwk", out var jwkEl))
            return StatusListBit.Unknown;

        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        if (!VerifySignature(signingInput, signature, jwkEl, alg))
            return StatusListBit.Unknown;

        // Decompress the zlib-packed bitstring and read the bit.
        if (!payload.TryGetProperty("status_list", out var statusList))
            return StatusListBit.Unknown;
        if (!statusList.TryGetProperty("lst", out var lstEl))
            return StatusListBit.Unknown;
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
            return StatusListBit.Unknown;
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
    /// Reads the bit at entry index <paramref name="idx"/> from a bitstring where
    /// each entry takes <paramref name="bitsPerEntry"/> bits. Returns
    /// <see cref="StatusListBit.Set"/> if any bit in the entry is set; otherwise
    /// <see cref="StatusListBit.NotSet"/>.
    /// </summary>
    internal static StatusListBit ReadBit(byte[] raw, int idx, int bitsPerEntry)
    {
        if (bitsPerEntry <= 0) return StatusListBit.Unknown;

        var startBit = (long)idx * bitsPerEntry;
        var endBit = startBit + bitsPerEntry;
        if (endBit > (long)raw.Length * 8)
            return StatusListBit.Unknown;

        for (var bit = startBit; bit < endBit; bit++)
        {
            var byteIdx = (int)(bit / 8);
            var bitIdx = (int)(bit % 8);
            // IETF & W3C use MSB-first bit ordering within a byte.
            if ((raw[byteIdx] & (1 << (7 - bitIdx))) != 0)
                return StatusListBit.Set;
        }
        return StatusListBit.NotSet;
    }
}

/// <summary>
/// Result of reading a bit from a status list bitstring.
/// </summary>
public enum StatusListBit
{
    /// <summary>Bit read successfully and is 0 — credential is active.</summary>
    NotSet = 0,
    /// <summary>Bit read successfully and is 1 — credential is revoked or suspended.</summary>
    Set = 1,
    /// <summary>Status could not be resolved (fetch failed, signature invalid, etc.).</summary>
    Unknown = 2,
}
