// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Derives a checksummed (EIP-55) Ethereum address from an secp256k1 public key. Phase-1 foundation
/// for later phases (did:pkh, address-form did:ethr, SIWE); not invoked by any Phase-1 verification path.
/// </summary>
public static class EthereumAddress
{
    /// <summary>
    /// Derive the EIP-55 checksummed <c>0x</c>-prefixed address: <c>keccak256(X || Y)[12..]</c>.
    /// </summary>
    public static string FromPublicKey(Secp256k1PublicKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Uncompressed SEC1 is 0x04 || X || Y; the address hashes the 64-byte X||Y body.
        var xy = key.ToSec1Uncompressed().AsSpan(1);
        var hash = Keccak256.ComputeHash(xy);
        var address = hash.AsSpan(12).ToArray(); // last 20 bytes
        return ToChecksumAddress(address);
    }

    private static string ToChecksumAddress(byte[] address20)
    {
        var lower = Convert.ToHexStringLower(address20); // 40 lowercase hex chars
        var hashHex = Convert.ToHexStringLower(Keccak256.ComputeHash(Encoding.ASCII.GetBytes(lower)));

        var sb = new StringBuilder(42);
        sb.Append("0x");
        for (var i = 0; i < lower.Length; i++)
        {
            var c = lower[i];
            // Uppercase the hex letter when the corresponding hash nibble is >= 8.
            if (c is >= 'a' and <= 'f' && hashHex[i] >= '8')
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
