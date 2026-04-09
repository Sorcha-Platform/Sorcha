// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using SimpleBase;

namespace Sorcha.ServiceClients.Http.Utilities;

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
/// PQC algorithms (ML-DSA, SLH-DSA, ML-KEM) do not yet have assigned multicodec identifiers,
/// so <see cref="ToMultibasePublicKey"/> returns <c>null</c> for them; callers must fall back
/// to a supported JWK shape or fail closed with a clear error (per FR-014 in spec 093).
/// </para>
/// <para>
/// Feature 093 US3 introduced this helper to fix the malformed multibase emitted by
/// <see cref="Did.SorchaDidResolver"/> on master, where <c>publicKeyMultibase</c> was the
/// literal string <c>"z"</c> concatenated with a hex/base64 public key.
/// </para>
/// <para>
/// Lives in <c>Sorcha.ServiceClients.Http</c> rather than <c>Sorcha.Cryptography</c> so that
/// mobile-friendly consumers of <c>Sorcha.ServiceClients.Http</c> do not transitively pull in
/// the full crypto assembly (Sodium.Core, BouncyCastle, MCL bindings).
/// </para>
/// </remarks>
public static class Multicodec
{
    // Multicodec identifiers from https://github.com/multiformats/multicodec/blob/master/table.csv
    private const int Ed25519PubCodec = 0xed;
    private const int P256PubCodec = 0x1200;
    private const int RsaPubCodec = 0x1205;

    /// <summary>
    /// Multibase base58btc prefix character.
    /// </summary>
    public const char Base58BtcPrefix = 'z';

    /// <summary>
    /// Returns the multicodec-prefixed public key bytes for the given algorithm,
    /// ready for base58btc encoding. The prefix is an unsigned varint.
    /// </summary>
    /// <param name="algorithmName">The algorithm name (e.g. "ED25519", "NIST-P256", "RSA-4096"). Case-insensitive.</param>
    /// <param name="rawKeyBytes">The raw public key bytes in the canonical form for the algorithm.</param>
    /// <returns>The multicodec-prefixed bytes, or <c>null</c> if the algorithm has no assigned multicodec identifier.</returns>
    public static byte[]? EncodePublicKey(string algorithmName, byte[] rawKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(algorithmName);
        ArgumentNullException.ThrowIfNull(rawKeyBytes);

        var codec = ResolveMulticodec(algorithmName);
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
    /// must fail closed or skip the field.
    /// </summary>
    /// <param name="algorithmName">The algorithm name (case-insensitive).</param>
    /// <param name="rawKeyBytes">The raw public key bytes.</param>
    /// <returns>The multibase-encoded string or <c>null</c>.</returns>
    public static string? ToMultibasePublicKey(string algorithmName, byte[] rawKeyBytes)
    {
        var prefixed = EncodePublicKey(algorithmName, rawKeyBytes);
        if (prefixed is null) return null;

        return Base58BtcPrefix + Base58.Bitcoin.Encode(prefixed);
    }

    /// <summary>
    /// Attempts to decode a stored public key string to raw bytes. Tries base64 first
    /// (canonical storage format per <c>WalletEndpoints.cs</c>); falls back to hex for
    /// any legacy wallets that may have hex-encoded public keys.
    /// </summary>
    public static byte[]? DecodePublicKeyBytes(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey)) return null;

        try
        {
            return Convert.FromBase64String(publicKey);
        }
        catch (FormatException)
        {
            // fall through
        }

        try
        {
            return Convert.FromHexString(publicKey);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a Sorcha algorithm name string to its multicodec identifier, or returns null if
    /// the algorithm has no assigned multicodec entry in the multiformats table.
    /// </summary>
    private static int? ResolveMulticodec(string? algorithmName) =>
        algorithmName?.ToUpperInvariant() switch
        {
            "ED25519" => Ed25519PubCodec,
            "NISTP256" or "NIST-P256" or "P-256" or "P256" or "ECDSA-P256" => P256PubCodec,
            "RSA" or "RSA4096" or "RSA-4096" => RsaPubCodec,
            _ => null
        };

    /// <summary>
    /// Encodes an unsigned integer using the unsigned LEB128 / multiformats varint encoding:
    /// 7 payload bits per byte, high bit set to 1 to indicate continuation.
    /// </summary>
    private static byte[] EncodeUnsignedVarint(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Multicodec identifier must be non-negative.");

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
