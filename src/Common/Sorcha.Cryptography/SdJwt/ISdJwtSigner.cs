// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Standalone JWS signer extracted so callers that build a JWS in pieces — notably
/// the Feature 120 sign-on-behalf path on the wallet service — can sign without
/// going through <see cref="ISdJwtService"/>'s full token-construction pipeline.
/// </summary>
public interface ISdJwtSigner
{
    /// <summary>
    /// Signs <paramref name="data"/> with <paramref name="privateKey"/> using
    /// <paramref name="algorithm"/>. Mirrors the algorithm support in <see cref="SdJwtService"/>.
    /// </summary>
    byte[] Sign(byte[] data, byte[] privateKey, string algorithm);
}

/// <summary>Default <see cref="ISdJwtSigner"/> implementation.</summary>
public sealed class SdJwtSigner : ISdJwtSigner
{
    /// <inheritdoc />
    public byte[] Sign(byte[] data, byte[] privateKey, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);

        var alg = algorithm.ToUpperInvariant();

        if (alg is "EDDSA" or "ED25519")
        {
            return Sodium.PublicKeyAuth.SignDetached(data, privateKey);
        }

        if (alg is "ES256" or "P-256" or "P256" or "NIST-P256" or "NISTP256")
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
}

/// <summary>Base64url helpers — public surface for components that build JWS pieces externally.</summary>
public static class Base64UrlHelper
{
    /// <summary>Base64url-encode without padding.</summary>
    public static string Encode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Base64url-decode (with or without padding).</summary>
    public static byte[] Decode(string base64Url)
    {
        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
