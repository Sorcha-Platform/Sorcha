// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text;
using System.Text.Json;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Provides deterministic hashing for blockchain dockets
/// </summary>
public class DocketHasher
{
    // Canonical hash-input serialization: exact property names, no whitespace. Hoisted to a single
    // shared instance (perf audit T10) — a fresh JsonSerializerOptions per call rebuilds an un-shared
    // converter/metadata cache. Output is byte-identical to the previous per-call options, so docket/
    // transaction hashes are unchanged (this MUST stay deterministic — it feeds the hash).
    private static readonly JsonSerializerOptions HashInputJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly IHashProvider _hashProvider;

    public DocketHasher(IHashProvider hashProvider)
    {
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <summary>
    /// Computes a deterministic hash for a docket
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketNumber">Docket number</param>
    /// <param name="previousHash">Previous docket hash (null for genesis)</param>
    /// <param name="merkleRoot">Merkle root of transactions</param>
    /// <param name="timestamp">Docket timestamp</param>
    /// <param name="hashType">Hash algorithm to use (default SHA256)</param>
    /// <returns>Docket hash as hex string</returns>
    public string ComputeDocketHash(
        string registerId,
        long docketNumber,
        string? previousHash,
        string merkleRoot,
        DateTimeOffset timestamp,
        HashType hashType = HashType.SHA256)
    {
        // Create deterministic JSON representation
        var hashInput = new
        {
            RegisterId = registerId,
            DocketNumber = docketNumber,
            PreviousHash = previousHash ?? string.Empty,
            MerkleRoot = merkleRoot,
            Timestamp = timestamp.ToUnixTimeMilliseconds() // Unix timestamp for determinism
        };

        var json = JsonSerializer.Serialize(hashInput, HashInputJsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = _hashProvider.ComputeHash(bytes, hashType);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a hash for transaction data (payload hash)
    /// </summary>
    /// <param name="payload">Transaction payload (JSON bytes)</param>
    /// <param name="hashType">Hash algorithm to use (default SHA256)</param>
    /// <returns>Payload hash as hex string</returns>
    public string ComputePayloadHash(byte[] payload, HashType hashType = HashType.SHA256)
    {
        if (payload == null || payload.Length == 0)
            throw new ArgumentException("Payload cannot be null or empty", nameof(payload));

        var hashBytes = _hashProvider.ComputeHash(payload, hashType);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a hash for a transaction (for Merkle tree leaves)
    /// </summary>
    /// <param name="transactionId">Transaction ID</param>
    /// <param name="payloadHash">Payload hash</param>
    /// <param name="timestamp">Transaction timestamp</param>
    /// <param name="hashType">Hash algorithm to use (default SHA256)</param>
    /// <returns>Transaction hash as hex string</returns>
    public string ComputeTransactionHash(
        string transactionId,
        string payloadHash,
        DateTimeOffset timestamp,
        HashType hashType = HashType.SHA256)
    {
        var hashInput = new
        {
            TransactionId = transactionId,
            PayloadHash = payloadHash,
            Timestamp = timestamp.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(hashInput, HashInputJsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = _hashProvider.ComputeHash(bytes, hashType);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies that a docket hash matches its computed hash
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketNumber">Docket number</param>
    /// <param name="previousHash">Previous docket hash</param>
    /// <param name="merkleRoot">Merkle root</param>
    /// <param name="timestamp">Docket timestamp</param>
    /// <param name="expectedHash">Expected docket hash</param>
    /// <param name="hashType">Hash algorithm used</param>
    /// <returns>True if hash matches</returns>
    public bool VerifyDocketHash(
        string registerId,
        long docketNumber,
        string? previousHash,
        string merkleRoot,
        DateTimeOffset timestamp,
        string expectedHash,
        HashType hashType = HashType.SHA256)
    {
        var computedHash = ComputeDocketHash(
            registerId,
            docketNumber,
            previousHash,
            merkleRoot,
            timestamp,
            hashType);

        return string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
