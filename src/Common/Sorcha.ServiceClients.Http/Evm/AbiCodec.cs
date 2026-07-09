// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using System.Text;
using Sorcha.Cryptography.Secp256k1;

namespace Sorcha.ServiceClients.Evm;

/// <summary>
/// Minimal, pure-managed Ethereum ABI helpers for the read-only ERC-1056 resolution path (Feature 179):
/// function selectors and event topic hashes (computed with the existing <see cref="Keccak256"/>
/// primitive — no ABI library), plus 32-byte word encode/decode. Not a general ABI implementation —
/// only what <c>eth_call</c>/<c>eth_getLogs</c> against the DID registry need.
/// </summary>
public static class AbiCodec
{
    /// <summary>The 4-byte function selector <c>0x</c>+8-hex for a canonical signature, e.g. <c>changed(address)</c>.</summary>
    public static string Selector(string signature)
    {
        var hash = Keccak256.ComputeHash(Encoding.ASCII.GetBytes(signature));
        return "0x" + Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    /// <summary>The 32-byte event topic hash <c>0x</c>+64-hex for a canonical event signature (uint → uint256).</summary>
    public static string EventTopic(string signature)
    {
        var hash = Keccak256.ComputeHash(Encoding.ASCII.GetBytes(signature));
        return "0x" + Convert.ToHexStringLower(hash);
    }

    /// <summary>Left-pad a 20-byte address to a 32-byte indexed-topic value (<c>0x</c>+24 zero-hex+40 hex, lowercased).</summary>
    public static string Pad32Topic(string address)
        => "0x" + new string('0', 24) + StripHex(address).ToLowerInvariant();

    /// <summary>ABI-encode an address as a 32-byte left-padded word.</summary>
    public static byte[] EncodeAddress(string address)
    {
        var word = new byte[32];
        Convert.FromHexString(StripHex(address)).CopyTo(word, 12);
        return word;
    }

    /// <summary>Decode the trailing 20-byte address from a 32-byte word (<c>0x</c>+40 lowercase hex).</summary>
    public static string DecodeAddress(ReadOnlySpan<byte> word)
        => "0x" + Convert.ToHexStringLower(word.Slice(12, 20));

    /// <summary>Decode a big-endian unsigned integer from a 32-byte word.</summary>
    public static BigInteger DecodeUInt(ReadOnlySpan<byte> word)
        => new(word, isUnsigned: true, isBigEndian: true);

    /// <summary>Decode a right-zero-padded ASCII string from a bytes32 word (e.g. <c>veriKey</c>).</summary>
    public static string DecodeBytes32Ascii(ReadOnlySpan<byte> word)
    {
        var end = word.Length;
        while (end > 0 && word[end - 1] == 0)
        {
            end--;
        }

        return Encoding.ASCII.GetString(word[..end]);
    }

    /// <summary>Extract the 32-byte word at index <paramref name="wordIndex"/> from ABI <paramref name="data"/>.</summary>
    public static ReadOnlySpan<byte> Word(ReadOnlySpan<byte> data, int wordIndex)
        => data.Slice(wordIndex * 32, 32);

    /// <summary>
    /// Decode a dynamic <c>bytes</c> value whose head word (at <paramref name="headWordIndex"/>) holds the
    /// byte offset to a <c>[length][data…]</c> tail. Returns the raw value bytes.
    /// </summary>
    public static byte[] DecodeDynamicBytes(ReadOnlySpan<byte> data, int headWordIndex)
    {
        var offset = (int)DecodeUInt(Word(data, headWordIndex));
        var length = (int)DecodeUInt(data.Slice(offset, 32));
        return data.Slice(offset + 32, length).ToArray();
    }

    private static string StripHex(string s)
        => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
}
