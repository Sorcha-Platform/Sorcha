// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Recovers candidate secp256k1 public keys from a JOSE ES256K signature (public-key recovery,
/// a.k.a. <c>ecrecover</c>). A JOSE ES256K signature is a 64-byte <c>r || s</c> with no recovery id,
/// so recovery tries recovery ids 0 and 1. Pure-managed (BouncyCastle); WASM-safe; verification-only.
/// </summary>
public static class Secp256k1Recovery
{
    /// <summary>
    /// Recover the candidate secp256k1 public keys that could have produced <paramref name="joseSignature"/>
    /// over <c>SHA-256(<paramref name="message"/>)</c>. Returns 0–2 valid, non-infinity candidates
    /// (recovery ids 0 and 1). Returns an empty list — never throws — for any malformed input.
    /// </summary>
    /// <param name="message">The JWS signing input bytes (<c>header.payload</c>); hashed with SHA-256 here.</param>
    /// <param name="joseSignature">The 64-byte fixed-width <c>r || s</c> JOSE ES256K signature.</param>
    public static IReadOnlyList<Secp256k1PublicKey> TryRecover(
        ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature)
    {
        if (joseSignature.Length != 64)
        {
            return [];
        }

        try
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(message, hash);

            var domain = Secp256k1PublicKey.Domain;
            var n = domain.N;
            var curve = domain.Curve;
            var g = domain.G;

            var r = new BigInteger(1, joseSignature[..32].ToArray());
            var s = new BigInteger(1, joseSignature[32..].ToArray());

            // r, s must be in [1, n-1].
            if (r.SignValue <= 0 || s.SignValue <= 0 || r.CompareTo(n) >= 0 || s.CompareTo(n) >= 0)
            {
                return [];
            }

            var e = new BigInteger(1, hash.ToArray());

            // Q = r^-1 (sR - eG) = (s·r^-1)·R + ((n - e)·r^-1)·G
            var rInv = r.ModInverse(n);
            var srInv = s.Multiply(rInv).Mod(n);
            var eInvNeg = n.Subtract(e).Mod(n).Multiply(rInv).Mod(n);

            // The candidate R points share x = r (recovery id >= 2, the r + n wrap, is negligibly
            // rare for secp256k1 and out of scope); recid's low bit selects the y parity.
            var xBytes = new byte[32];
            var rEnc = r.ToByteArrayUnsigned();
            Array.Copy(rEnc, 0, xBytes, 32 - rEnc.Length, rEnc.Length);

            var results = new List<Secp256k1PublicKey>(2);
            for (var parity = 0; parity < 2; parity++)
            {
                try
                {
                    var compressed = new byte[33];
                    compressed[0] = (byte)(0x02 | parity);
                    Array.Copy(xBytes, 0, compressed, 1, 32);

                    var pointR = curve.DecodePoint(compressed);
                    if (!pointR.IsValid())
                    {
                        continue;
                    }

                    var q = ECAlgorithms.SumOfTwoMultiplies(pointR, srInv, g, eInvNeg).Normalize();
                    if (q.IsInfinity || !q.IsValid())
                    {
                        continue;
                    }

                    results.Add(Secp256k1PublicKey.FromSec1(q.GetEncoded(false)));
                }
                catch
                {
                    // This recovery id yields no valid candidate (x not on curve, etc.); try the other.
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }
}
