// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Minimal RLP (Recursive Length Prefix) encoder for Ethereum transaction serialisation (Feature 182).
/// Encoding only — no decoder is needed to build and sign a transaction. Pure-managed; WASM-safe.
/// The subtle rule is scalar encoding: integers are minimal big-endian byte strings with leading zeros
/// stripped, and the value <c>0</c> encodes as the empty byte string (<c>0x80</c>), never <c>0x00</c>.
/// </summary>
public static class Rlp
{
    /// <summary>
    /// RLP-encode a byte string: a single byte below <c>0x80</c> is itself; a string of length ≤ 55 is
    /// prefixed with <c>0x80 + length</c>; a longer string is prefixed with <c>0xb7 + lengthOfLength</c>
    /// then the big-endian length. An empty string encodes as <c>0x80</c>.
    /// </summary>
    public static byte[] EncodeString(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 1 && bytes[0] < 0x80)
        {
            return [bytes[0]];
        }

        return Concat(EncodeLength(bytes.Length, 0x80), bytes.ToArray());
    }

    /// <summary>
    /// RLP-encode a non-negative integer as a minimal big-endian scalar byte string (leading zeros
    /// stripped; <c>0</c> → the empty string <c>0x80</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static byte[] EncodeScalar(BigInteger value)
    {
        if (value.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "RLP scalars must be non-negative.");
        }

        return EncodeString(ToMinimalBigEndian(value));
    }

    /// <summary>
    /// RLP-encode a list from already-RLP-encoded items: the concatenated items are prefixed with
    /// <c>0xc0 + payloadLength</c> (≤ 55) or <c>0xf7 + lengthOfLength</c> then the big-endian length.
    /// An empty list encodes as <c>0xc0</c>.
    /// </summary>
    public static byte[] EncodeList(params byte[][] encodedItems)
    {
        var payloadLength = 0;
        foreach (var item in encodedItems)
        {
            payloadLength += item.Length;
        }

        var payload = new byte[payloadLength];
        var offset = 0;
        foreach (var item in encodedItems)
        {
            Array.Copy(item, 0, payload, offset, item.Length);
            offset += item.Length;
        }

        return Concat(EncodeLength(payloadLength, 0xc0), payload);
    }

    /// <summary>Strip leading zero bytes from a big-endian integer; <c>0</c> → empty span.</summary>
    internal static byte[] ToMinimalBigEndian(BigInteger value)
    {
        if (value.IsZero)
        {
            return [];
        }

        // BigInteger is little-endian and unsigned here; reverse and strip the sign padding byte.
        var le = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        var length = le.Length;
        while (length > 1 && le[length - 1] == 0)
        {
            length--;
        }

        var be = new byte[length];
        for (var i = 0; i < length; i++)
        {
            be[i] = le[length - 1 - i];
        }

        return be;
    }

    private static byte[] EncodeLength(int length, byte offset)
    {
        if (length <= 55)
        {
            return [(byte)(offset + length)];
        }

        var lengthBytes = ToMinimalBigEndian(new BigInteger(length));
        var header = new byte[1 + lengthBytes.Length];
        header[0] = (byte)(offset + 55 + lengthBytes.Length);
        Array.Copy(lengthBytes, 0, header, 1, lengthBytes.Length);
        return header;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Array.Copy(a, 0, result, 0, a.Length);
        Array.Copy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
