// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Serializes a raw bitstring into an IETF Token Status List JWT envelope.
/// The JWT has typ=statuslist+jwt and payload containing status_list: { bits, lst }.
/// </summary>
public interface IIetfTokenStatusListSerializer
{
    /// <summary>
    /// Produces a signed JWT envelope for the given raw bitstring.
    /// </summary>
    /// <param name="rawBitstring">Decompressed bitstring bytes (shared with W3C).</param>
    /// <param name="listId">Status list identifier.</param>
    /// <param name="issuerDid">DID of the list issuer (e.g., "did:sorcha:org:ws1q...").</param>
    /// <param name="bitsPerEntry">Bit width per entry (1 for revocation, 2 for suspension+revocation).</param>
    /// <param name="signingKey">Private key bytes for signing the envelope JWT.</param>
    /// <param name="algorithm">Signing algorithm (e.g., "ES256", "EdDSA").</param>
    /// <param name="ttlSeconds">Cache TTL — the envelope JWT's exp is iat + ttl.</param>
    /// <returns>The serialized JWT string.</returns>
    string Serialize(
        byte[] rawBitstring,
        string listId,
        string issuerDid,
        int bitsPerEntry,
        byte[] signingKey,
        string algorithm,
        int ttlSeconds = 300);
}

/// <inheritdoc />
public class IetfTokenStatusListSerializer : IIetfTokenStatusListSerializer
{
    /// <inheritdoc />
    public string Serialize(
        byte[] rawBitstring,
        string listId,
        string issuerDid,
        int bitsPerEntry,
        byte[] signingKey,
        string algorithm,
        int ttlSeconds = 300)
    {
        ArgumentNullException.ThrowIfNull(rawBitstring);
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDid);
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);

        // ZLib compress the raw bitstring (IETF uses DEFLATE/ZLib, not GZip)
        var zlibCompressed = ZLibCompress(rawBitstring);
        var lst = Base64UrlEncode(zlibCompressed);

        var now = DateTimeOffset.UtcNow;

        // Build JWT header
        var header = new Dictionary<string, object>
        {
            ["alg"] = MapAlgorithm(algorithm),
            ["typ"] = "statuslist+jwt"
        };

        // Build JWT payload per IETF Token Status List draft
        var payload = new Dictionary<string, object>
        {
            ["iss"] = issuerDid,
            ["sub"] = listId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(ttlSeconds).ToUnixTimeSeconds(),
            ["ttl"] = ttlSeconds,
            ["status_list"] = new Dictionary<string, object>
            {
                ["bits"] = bitsPerEntry,
                ["lst"] = lst
            }
        };

        // Sign the JWT
        var headerB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        var signature = Sign(signingInput, signingKey, algorithm);
        var signatureB64 = Base64UrlEncode(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    /// <summary>
    /// Compresses raw bytes using ZLib (DEFLATE with zlib header).
    /// </summary>
    internal static byte[] ZLibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Decompresses ZLib-compressed bytes.
    /// </summary>
    internal static byte[] ZLibDecompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string MapAlgorithm(string algorithm)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "EDDSA" or "ED25519" => "EdDSA",
            "ES256" or "P-256" or "P256" => "ES256",
            "RS256" or "RSA" or "RSA-4096" => "RS256",
            _ => algorithm
        };
    }

    private static byte[] Sign(byte[] data, byte[] privateKey, string algorithm)
    {
        var alg = algorithm.ToUpperInvariant();

        if (alg is "EDDSA" or "ED25519")
        {
            return Sodium.PublicKeyAuth.SignDetached(data, privateKey);
        }

        if (alg is "ES256" or "P-256" or "P256")
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportECPrivateKey(privateKey, out _);
            return ecdsa.SignData(data, HashAlgorithmName.SHA256);
        }

        if (alg is "RS256" or "RSA" or "RSA-4096")
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(privateKey, out _);
            return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        throw new NotSupportedException($"Unsupported signing algorithm: {algorithm}");
    }

    private static string Base64UrlEncode(byte[] data) =>
        Base64Url.EncodeToString(data);
}
