// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.TransactionHandler.Encryption;

/// <summary>
/// Derives per-chunk encryption keys from a master file key using HKDF-SHA256.
/// Each chunk receives a unique key derived from the master key, a random salt, and the chunk index.
/// </summary>
public static class HkdfKeyDerivation
{
    /// <summary>The required size in bytes for master file keys and derived chunk keys (XChaCha20-Poly1305).</summary>
    public const int KeySizeBytes = 32;

    /// <summary>The HKDF info prefix used to bind derived keys to their chunk context.</summary>
    public const string InfoPrefix = "sorcha-chunk-";

    /// <summary>
    /// Derives a 32-byte encryption key for a specific file chunk.
    /// </summary>
    /// <param name="masterFileKey">The 32-byte master file key. Must be exactly 32 bytes.</param>
    /// <param name="salt">The random salt from the FileReference. Used as HKDF salt input.</param>
    /// <param name="chunkIndex">The zero-based index of the chunk. Must be non-negative.</param>
    /// <returns>A 32-byte derived key unique to this chunk.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="masterFileKey"/> is not exactly 32 bytes,
    /// or when <paramref name="chunkIndex"/> is negative.
    /// </exception>
    public static byte[] DeriveChunkKey(byte[] masterFileKey, byte[] salt, int chunkIndex)
    {
        if (masterFileKey is null || masterFileKey.Length != KeySizeBytes)
            throw new ArgumentException($"Master file key must be exactly {KeySizeBytes} bytes.", nameof(masterFileKey));

        if (chunkIndex < 0)
            throw new ArgumentException("Chunk index must be non-negative.", nameof(chunkIndex));

        var info = Encoding.UTF8.GetBytes($"{InfoPrefix}{chunkIndex}");

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterFileKey, KeySizeBytes, salt, info);
    }

    /// <summary>
    /// Derives a 32-byte encryption key for a specific file chunk into a caller-supplied buffer.
    /// This span-based overload avoids heap allocation in hot paths.
    /// </summary>
    /// <param name="masterFileKey">The 32-byte master file key as a read-only span. Must be exactly 32 bytes.</param>
    /// <param name="salt">The random salt from the FileReference as a read-only span.</param>
    /// <param name="chunkIndex">The zero-based index of the chunk. Must be non-negative.</param>
    /// <param name="output">The destination span to receive the derived key. Must be exactly 32 bytes.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="masterFileKey"/> is not exactly 32 bytes,
    /// when <paramref name="chunkIndex"/> is negative,
    /// or when <paramref name="output"/> is not exactly 32 bytes.
    /// </exception>
    public static void DeriveChunkKey(ReadOnlySpan<byte> masterFileKey, ReadOnlySpan<byte> salt, int chunkIndex, Span<byte> output)
    {
        if (masterFileKey.Length != KeySizeBytes)
            throw new ArgumentException($"Master file key must be exactly {KeySizeBytes} bytes.", nameof(masterFileKey));

        if (chunkIndex < 0)
            throw new ArgumentException("Chunk index must be non-negative.", nameof(chunkIndex));

        if (output.Length != KeySizeBytes)
            throw new ArgumentException($"Output span must be exactly {KeySizeBytes} bytes.", nameof(output));

        // "sorcha-chunk-" is 13 chars; chunk index is at most 2 decimal digits (max 10 chunks) = 15 max.
        // stackalloc avoids a heap allocation on every per-chunk key derivation in hot paths.
        Span<byte> info = stackalloc byte[32];
        int written = Encoding.UTF8.GetBytes($"{InfoPrefix}{chunkIndex}", info);
        info = info[..written];

        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterFileKey, output, salt, info);
    }

    /// <summary>
    /// Generates a cryptographically random 32-byte master file key.
    /// </summary>
    /// <returns>A new 32-byte random master file key.</returns>
    public static byte[] GenerateMasterFileKey()
    {
        var key = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Generates a cryptographically random 32-byte salt for use in HKDF key derivation.
    /// </summary>
    /// <returns>A new 32-byte random salt.</returns>
    public static byte[] GenerateSalt()
    {
        var salt = new byte[KeySizeBytes];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }
}
