// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using Sorcha.Cryptography.Enums;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Multicodec prefix encoding for W3C DID Core <c>publicKeyMultibase</c> values.
/// </summary>
/// <remarks>
/// <para>
/// The W3C DID Core specification expects <c>publicKeyMultibase</c> to be encoded as
/// <c>"z" + Base58btc(multicodec || rawKeyBytes)</c>, where the multicodec identifier is
/// an unsigned varint drawn from the multiformats/multicodec table and <c>rawKeyBytes</c>
/// is the public key in its canonical binary form for the algorithm.
/// </para>
/// <para>
/// This helper covers the three classical algorithms currently in the Sorcha wallet model.
/// PQC algorithms (ML-DSA, SLH-DSA, ML-KEM) do not yet have assigned multicodec identifiers
/// in the table, so <see cref="ToMultibasePublicKey"/> returns <c>null</c> for them; callers
/// must fall back to <c>publicKeyJwk</c> or fail closed with a clear error.
/// </para>
/// <para>Feature 093 US3 introduced this helper to fix the malformed multibase emitted by
/// <c>SorchaDidResolver</c> on master, where <c>publicKeyMultibase</c> was the literal string
/// <c>"z"</c> concatenated with a hex/base64 public key — not valid W3C multibase.</para>
/// </remarks>
public static class Multicodec
{
    // Multicodec identifiers from https://github.com/multiformats/multicodec/blob/master/table.csv
    // These are the raw identifier values — EncodePublicKey serialises them as unsigned varints
    // before prepending to the raw key bytes, which is what multibase consumers expect.
    private const int Ed25519Pub = 0xed;
    private const int P256Pub = 0x1200;
    private const int RsaPub = 0x1205;

    /// <summary>
    /// Multibase base58btc prefix character.
    /// </summary>
    public const char Base58BtcPrefix = 'z';

    /// <summary>
    /// Returns the multicodec-prefixed public key bytes for the given algorithm,
    /// ready for base58btc encoding. The prefix is an unsigned varint.
    /// </summary>
    /// <param name="algorithm">The wallet algorithm.</param>
    /// <param name="rawKeyBytes">The raw public key bytes in the canonical form for the algorithm.</param>
    /// <returns>The multicodec-prefixed bytes, or <c>null</c> if the algorithm has no assigned multicodec identifier.</returns>
    public static byte[]? EncodePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(rawKeyBytes);

        var codec = algorithm switch
        {
            WalletNetworks.ED25519 => (int?)Ed25519Pub,
            WalletNetworks.NISTP256 => P256Pub,
            WalletNetworks.RSA4096 => RsaPub,
            _ => null
        };

        if (codec is null) return null;

        var varintBytes = EncodeUnsignedVarint(codec.Value);
        var result = new byte[varintBytes.Length + rawKeyBytes.Length];
        Buffer.BlockCopy(varintBytes, 0, result, 0, varintBytes.Length);
        Buffer.BlockCopy(rawKeyBytes, 0, result, varintBytes.Length, rawKeyBytes.Length);
        return result;
    }

    /// <summary>
    /// Returns a full W3C <c>publicKeyMultibase</c> string: <c>"z" + Base58btc(multicodec || rawKeyBytes)</c>.
    /// Returns <c>null</c> for algorithms that have no assigned multicodec identifier — callers
    /// must fall back to <c>publicKeyJwk</c> or fail closed.
    /// </summary>
    /// <param name="algorithm">The wallet algorithm.</param>
    /// <param name="rawKeyBytes">The raw public key bytes.</param>
    /// <returns>The multibase-encoded string or <c>null</c>.</returns>
    public static string? ToMultibasePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)
    {
        var prefixed = EncodePublicKey(algorithm, rawKeyBytes);
        if (prefixed is null) return null;

        var base58 = Base58.Encode(prefixed);
        return Base58BtcPrefix + base58;
    }

    /// <summary>
    /// Encodes an unsigned integer using the unsigned LEB128 / multiformats varint encoding:
    /// 7 payload bits per byte, high bit set to 1 to indicate continuation.
    /// </summary>
    private static byte[] EncodeUnsignedVarint(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Multicodec identifier must be non-negative.");

        // Maximum varint length for a 32-bit value is 5 bytes.
        Span<byte> buffer = stackalloc byte[5];
        var length = 0;
        var remaining = (uint)value;

        while (remaining >= 0x80)
        {
            buffer[length++] = (byte)((remaining & 0x7f) | 0x80);
            remaining >>= 7;
        }
        buffer[length++] = (byte)remaining;

        return buffer[..length].ToArray();
    }
}
