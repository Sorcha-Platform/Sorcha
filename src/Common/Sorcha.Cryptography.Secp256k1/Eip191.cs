// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// ERC-191 / personal_sign message digest (Feature 180). The Ethereum ecosystem signs the keccak256 of
/// a prefixed message rather than the raw bytes, so a signature can only ever authenticate a human-facing
/// message — never a transaction. Used by the SIWE (EIP-4361) prove-control path.
/// </summary>
public static class Eip191
{
    // "Ethereum Signed Message:\n" — the 0x19 leading byte is prepended separately below.
    private const string PrefixText = "Ethereum Signed Message:\n";

    /// <summary>
    /// Compute the personal_sign digest: keccak256(0x19 + "Ethereum Signed Message:\n" + length + message).
    /// </summary>
    public static byte[] PersonalSignDigest(ReadOnlySpan<byte> message)
    {
        var prefix = Encoding.ASCII.GetBytes(PrefixText + message.Length);
        var preimage = new byte[1 + prefix.Length + message.Length];
        preimage[0] = 0x19;
        prefix.CopyTo(preimage, 1);
        message.CopyTo(preimage.AsSpan(1 + prefix.Length));
        return Keccak256.ComputeHash(preimage);
    }
}
