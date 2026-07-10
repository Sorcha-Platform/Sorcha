// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Recovers candidate secp256k1 public keys from a signature (public-key recovery, a.k.a.
/// <c>ecrecover</c>). <see cref="TryRecover"/> is the JOSE ES256K path (message hashed with SHA-256);
/// <see cref="RecoverFromDigest"/> is the Ethereum path (the caller supplies the raw keccak/EIP-191
/// digest directly). Pure-managed (BouncyCastle); WASM-safe.
/// </summary>
public static class Secp256k1Recovery
{
    /// <summary>
    /// Recover the candidate secp256k1 public keys for a JOSE ES256K signature over
    /// <c>SHA-256(<paramref name="message"/>)</c> (recovery ids 0 and 1). Empty on malformed input; never throws.
    /// </summary>
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
            var e = new BigInteger(1, hash.ToArray());

            var r = new BigInteger(1, joseSignature[..32].ToArray());
            var s = new BigInteger(1, joseSignature[32..].ToArray());

            var results = new List<Secp256k1PublicKey>(2);
            for (var recId = 0; recId < 2; recId++)
            {
                var key = RecoverCore(e, r, s, recId);
                if (key is not null)
                {
                    results.Add(key);
                }
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Recover the secp256k1 public key for the Ethereum-style signature <c>(r, s, recoveryId)</c> over
    /// the raw 32-byte <paramref name="digest"/> (keccak / EIP-191 — <b>not</b> re-hashed). Returns null
    /// on invalid/off-curve input; never throws.
    /// </summary>
    public static Secp256k1PublicKey? RecoverFromDigest(
        ReadOnlySpan<byte> digest, BigInteger r, BigInteger s, int recoveryId)
    {
        if (digest.Length != 32 || recoveryId is < 0 or > 1)
        {
            return null;
        }

        try
        {
            var e = new BigInteger(1, digest.ToArray());
            return RecoverCore(e, r, s, recoveryId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// SEC1 §4.1.6 public-key recovery for a single recovery id: reconstruct <c>R</c> from <c>r</c> with
    /// the recovery-id parity, then <c>Q = r⁻¹(sR − eG)</c>. Returns null for an invalid candidate.
    /// </summary>
    private static Secp256k1PublicKey? RecoverCore(BigInteger e, BigInteger r, BigInteger s, int recId)
    {
        var domain = Secp256k1PublicKey.Domain;
        var n = domain.N;

        if (r.SignValue <= 0 || s.SignValue <= 0 || r.CompareTo(n) >= 0 || s.CompareTo(n) >= 0)
        {
            return null;
        }

        // Reconstruct R: x = r (the recId >= 2 "r + n" wrap is negligibly rare for secp256k1 and out of
        // scope); recId's low bit selects the y parity.
        var xBytes = new byte[32];
        var rEnc = r.ToByteArrayUnsigned();
        Array.Copy(rEnc, 0, xBytes, 32 - rEnc.Length, rEnc.Length);

        try
        {
            var compressed = new byte[33];
            compressed[0] = (byte)(0x02 | (recId & 1));
            Array.Copy(xBytes, 0, compressed, 1, 32);

            var pointR = domain.Curve.DecodePoint(compressed);
            if (!pointR.IsValid())
            {
                return null;
            }

            var rInv = r.ModInverse(n);
            var srInv = s.Multiply(rInv).Mod(n);
            var eInvNeg = n.Subtract(e).Mod(n).Multiply(rInv).Mod(n);

            var q = ECAlgorithms.SumOfTwoMultiplies(pointR, srInv, domain.G, eInvNeg).Normalize();
            if (q.IsInfinity || !q.IsValid())
            {
                return null;
            }

            return Secp256k1PublicKey.FromSec1(q.GetEncoded(false));
        }
        catch
        {
            return null;
        }
    }
}
