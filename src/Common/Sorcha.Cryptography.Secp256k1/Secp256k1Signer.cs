// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Produces recoverable secp256k1 signatures over a 32-byte digest for Ethereum prove-control (Feature
/// 180). Deterministic RFC-6979 nonce (no RNG nonce-reuse key leak), low-s canonical, 65-byte
/// <c>r‖s‖v</c> output (<c>v = recoveryId + 27</c>). Signs the digest directly — Ethereum ECDSA hashes
/// with keccak (via <see cref="Eip191"/>), so this never re-hashes. Pure-managed (BouncyCastle); WASM-safe.
/// </summary>
public static class Secp256k1Signer
{
    /// <summary>
    /// Sign a 32-byte <paramref name="digest"/> with the 32-byte secp256k1 <paramref name="privateKey"/>,
    /// returning a 65-byte recoverable signature <c>r(32)‖s(32)‖v(1)</c> (low-s; <c>v ∈ {27,28}</c>).
    /// </summary>
    /// <exception cref="ArgumentException">A length or range precondition is violated.</exception>
    public static byte[] SignRecoverable(ReadOnlySpan<byte> digest, ReadOnlySpan<byte> privateKey)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("Digest must be 32 bytes.", nameof(digest));
        }

        if (privateKey.Length != 32)
        {
            throw new ArgumentException("Private key must be 32 bytes.", nameof(privateKey));
        }

        var domain = Secp256k1PublicKey.Domain;
        var n = domain.N;
        var d = new BigInteger(1, privateKey.ToArray());
        if (d.SignValue <= 0 || d.CompareTo(n) >= 0)
        {
            throw new ArgumentException("Private key is out of range.", nameof(privateKey));
        }

        var digestArray = digest.ToArray();
        var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
        signer.Init(true, new ECPrivateKeyParameters(d, domain));
        var rs = signer.GenerateSignature(digestArray);
        var r = rs[0];
        var s = rs[1];

        // Low-s canonical form (Ethereum ecosystem requirement).
        if (s.CompareTo(n.ShiftRight(1)) > 0)
        {
            s = n.Subtract(s);
        }

        // Determine the recovery id by matching the recovered key to this key's public key.
        var expectedHex = Convert.ToHexString(
            Secp256k1PublicKey.FromSec1(domain.G.Multiply(d).Normalize().GetEncoded(false)).ToSec1Uncompressed());

        var v = -1;
        for (var recId = 0; recId < 2; recId++)
        {
            var candidate = Secp256k1Recovery.RecoverFromDigest(digest, r, s, recId);
            if (candidate is not null
                && Convert.ToHexString(candidate.ToSec1Uncompressed()) == expectedHex)
            {
                v = recId + 27;
                break;
            }
        }

        if (v < 0)
        {
            throw new InvalidOperationException("Could not determine the signature recovery id.");
        }

        var signature = new byte[65];
        ToFixed32(r).CopyTo(signature, 0);
        ToFixed32(s).CopyTo(signature, 32);
        signature[64] = (byte)v;
        return signature;
    }

    private static byte[] ToFixed32(BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length == 32)
        {
            return bytes;
        }

        var result = new byte[32];
        Array.Copy(bytes, 0, result, 32 - bytes.Length, bytes.Length);
        return result;
    }
}
