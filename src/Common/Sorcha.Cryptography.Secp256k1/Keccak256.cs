// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Digests;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Keccak-256 (the original Keccak padding used by Ethereum, distinct from NIST SHA3-256).
/// Phase-1 foundation for Ethereum address derivation; not invoked by any Phase-1 verification path.
/// </summary>
public static class Keccak256
{
    /// <summary>Compute the 32-byte Keccak-256 digest of <paramref name="data"/>.</summary>
    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        var digest = new KeccakDigest(256);
        var input = data.ToArray();
        digest.BlockUpdate(input, 0, input.Length);
        var output = new byte[32];
        digest.DoFinal(output, 0);
        return output;
    }
}
