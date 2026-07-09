// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>Verifies JOSE ES256K signatures against an secp256k1 public key.</summary>
public interface ISecp256k1Verifier
{
    /// <summary>
    /// Verify a JOSE ES256K signature (ECDSA over SHA-256 with a 64-byte fixed-width <c>r || s</c>)
    /// against <paramref name="key"/>. Returns <c>false</c> — never throws — for any malformed input.
    /// </summary>
    bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key);
}

/// <summary>
/// Pure-managed (BouncyCastle) JOSE ES256K verifier. Accepts both high-s and low-s signatures on
/// verification (low-s is a produce-side concern deferred to a later phase). WASM-safe.
/// </summary>
public sealed class Secp256k1Verifier : ISecp256k1Verifier
{
    /// <summary>A shared stateless instance for call sites that do not use dependency injection.</summary>
    public static readonly Secp256k1Verifier Default = new();

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key)
        => VerifyEs256k(message, joseSignature, key);

    /// <summary>
    /// Static verification entry point for the static call sites (e.g. <c>SdJwtService.Verify</c>).
    /// </summary>
    public static bool VerifyEs256k(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key)
    {
        if (key is null || joseSignature.Length != 64)
        {
            return false;
        }

        try
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(message, hash);

            var r = new BigInteger(1, joseSignature[..32].ToArray());
            var s = new BigInteger(1, joseSignature[32..].ToArray());
            if (r.SignValue <= 0 || s.SignValue <= 0)
            {
                return false;
            }

            var signer = new ECDsaSigner();
            signer.Init(false, new ECPublicKeyParameters(key.Point, Secp256k1PublicKey.Domain));
            return signer.VerifySignature(hash.ToArray(), r, s);
        }
        catch
        {
            return false;
        }
    }
}
