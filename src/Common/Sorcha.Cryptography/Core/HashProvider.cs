// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sodium;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Extensions;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Implements cryptographic hash functions.
/// </summary>
public class HashProvider : IHashProvider
{
    /// <summary>
    /// Computes hash of data using specified algorithm.
    /// </summary>
    public byte[] ComputeHash(byte[] data, HashType hashType = HashType.SHA256)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        return hashType switch
        {
            HashType.SHA256 => SHA256.HashData(data),
            HashType.SHA384 => SHA384.HashData(data),
            HashType.SHA512 => SHA512.HashData(data),
            HashType.Blake2b256 => ComputeBlake2b(data, 32),
            HashType.Blake2b512 => ComputeBlake2b(data, 64),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}", nameof(hashType))
        };
    }

    /// <summary>
    /// Computes hash of data stream using specified algorithm.
    /// </summary>
    public async Task<byte[]> ComputeHashAsync(
        Stream dataStream,
        HashType hashType = HashType.SHA256,
        CancellationToken cancellationToken = default)
    {
        if (dataStream == null)
            throw new ArgumentNullException(nameof(dataStream));

        if (!dataStream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(dataStream));

        return hashType switch
        {
            HashType.SHA256 => await SHA256.HashDataAsync(dataStream, cancellationToken),
            HashType.SHA384 => await SHA384.HashDataAsync(dataStream, cancellationToken),
            HashType.SHA512 => await SHA512.HashDataAsync(dataStream, cancellationToken),
            HashType.Blake2b256 => await ComputeBlake2bStreamAsync(dataStream, 32, cancellationToken),
            HashType.Blake2b512 => await ComputeBlake2bStreamAsync(dataStream, 64, cancellationToken),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}", nameof(hashType))
        };
    }

    /// <summary>
    /// Computes HMAC of data.
    /// </summary>
    public byte[] ComputeHMAC(byte[] data, byte[] key, HashType hashType = HashType.SHA256)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        if (key == null || key.Length == 0)
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        return hashType switch
        {
            HashType.SHA256 => HMACSHA256.HashData(key, data),
            HashType.SHA384 => HMACSHA384.HashData(key, data),
            HashType.SHA512 => HMACSHA512.HashData(key, data),
            HashType.Blake2b256 => ComputeBlake2bHMAC(data, key, 32),
            HashType.Blake2b512 => ComputeBlake2bHMAC(data, key, 64),
            _ => throw new ArgumentException($"Unsupported hash type: {hashType}", nameof(hashType))
        };
    }

    /// <summary>
    /// Verifies that data matches a given hash.
    /// </summary>
    public bool VerifyHash(byte[] data, byte[] hash, HashType hashType = HashType.SHA256)
    {
        if (data == null || hash == null)
            return false;

        try
        {
            byte[] computedHash = ComputeHash(data, hashType);

            // Constant-time comparison to prevent timing attacks
            return CryptographicOperations.FixedTimeEquals(computedHash, hash);
        }
        catch
        {
            return false;
        }
    }

    private byte[] ComputeBlake2b(byte[] data, int hashSize)
    {
        if (hashSize != 32 && hashSize != 64)
            throw new ArgumentException("Blake2b hash size must be 32 or 64 bytes", nameof(hashSize));

        // Use libsodium's Blake2b implementation with custom output length
        return GenericHash.Hash(data, null, hashSize);
    }

    private async Task<byte[]> ComputeBlake2bStreamAsync(Stream stream, int hashSize, CancellationToken cancellationToken)
    {
        if (hashSize != 32 && hashSize != 64)
            throw new ArgumentException("Blake2b hash size must be 32 or 64 bytes", nameof(hashSize));

        // Read stream into memory and hash
        // For very large streams, a streaming implementation would be better
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        byte[] data = memoryStream.ToArray();

        return ComputeBlake2b(data, hashSize);
    }

    private byte[] ComputeBlake2bHMAC(byte[] data, byte[] key, int hashSize)
    {
        if (hashSize != 32 && hashSize != 64)
            throw new ArgumentException("Blake2b hash size must be 32 or 64 bytes", nameof(hashSize));

        // Use libsodium's Blake2b with key (HMAC-like functionality)
        // GenericHash.Hash supports keyed hashing natively
        return GenericHash.Hash(data, key, hashSize);
    }

    /// <summary>
    /// Computes double SHA-256 hash (used in Bitcoin).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The double hash.</returns>
    public byte[] ComputeDoubleSHA256(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        byte[] firstHash = SHA256.HashData(data);
        return SHA256.HashData(firstHash);
    }

    /// <summary>
    /// Computes RIPEMD-160 hash of SHA-256 (used in Bitcoin addresses).
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>The hash.</returns>
    public byte[] ComputeSHA256RIPEMD160(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Data cannot be null or empty", nameof(data));

        // SHA-256 first
        byte[] sha256Hash = SHA256.HashData(data);

        // Then RIPEMD-160 using BouncyCastle
        var ripemd160 = new Org.BouncyCastle.Crypto.Digests.RipeMD160Digest();
        ripemd160.BlockUpdate(sha256Hash, 0, sha256Hash.Length);
        byte[] result = new byte[ripemd160.GetDigestSize()];
        ripemd160.DoFinal(result, 0);
        return result;
    }

}
