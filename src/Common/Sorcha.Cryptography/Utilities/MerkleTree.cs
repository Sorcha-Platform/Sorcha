// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Provides Merkle tree construction and verification for blockchain dockets
/// </summary>
public class MerkleTree
{
    private readonly IHashProvider _hashProvider;

    public MerkleTree(IHashProvider hashProvider)
    {
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <summary>
    /// Computes the Merkle root from a list of transaction hashes
    /// </summary>
    /// <param name="transactionHashes">List of transaction hashes (hex strings)</param>
    /// <param name="hashType">Hash algorithm to use (default SHA256)</param>
    /// <returns>Merkle root as hex string</returns>
    public string ComputeMerkleRoot(IReadOnlyList<string> transactionHashes, HashType hashType = HashType.SHA256)
    {
        if (transactionHashes == null || transactionHashes.Count == 0)
        {
            // Empty Merkle root (hash of empty string)
            var emptyHash = _hashProvider.ComputeHash(Array.Empty<byte>(), hashType);
            return Convert.ToHexString(emptyHash).ToLowerInvariant();
        }

        // Single transaction - return its hash
        if (transactionHashes.Count == 1)
        {
            return transactionHashes[0].ToLowerInvariant();
        }

        // Build Merkle tree from leaf hashes
        var currentLevel = transactionHashes.Select(h => h.ToLowerInvariant()).ToList();

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();

            // Pair up hashes and combine them
            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                string left = currentLevel[i];
                string right;

                // If odd number of hashes, duplicate the last one
                if (i + 1 < currentLevel.Count)
                {
                    right = currentLevel[i + 1];
                }
                else
                {
                    right = left;
                }

                // Combine and hash the pair
                string combined = CombineAndHash(left, right, hashType);
                nextLevel.Add(combined);
            }

            currentLevel = nextLevel;
        }

        return currentLevel[0];
    }

    /// <summary>
    /// Computes the Merkle root from a list of data items (e.g., transaction objects)
    /// </summary>
    /// <param name="dataItems">List of data items to hash</param>
    /// <param name="hashType">Hash algorithm to use (default SHA256)</param>
    /// <returns>Merkle root as hex string</returns>
    public string ComputeMerkleRootFromData(IReadOnlyList<byte[]> dataItems, HashType hashType = HashType.SHA256)
    {
        if (dataItems == null || dataItems.Count == 0)
        {
            var emptyHash = _hashProvider.ComputeHash(Array.Empty<byte>(), hashType);
            return Convert.ToHexString(emptyHash).ToLowerInvariant();
        }

        // Hash each data item to create leaf hashes
        var leafHashes = dataItems.Select(data =>
        {
            var hash = _hashProvider.ComputeHash(data, hashType);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }).ToList();

        return ComputeMerkleRoot(leafHashes, hashType);
    }

    /// <summary>
    /// Verifies that a data item is part of a Merkle tree with a given root
    /// </summary>
    /// <param name="dataHash">Hash of the data item</param>
    /// <param name="merkleRoot">Expected Merkle root</param>
    /// <param name="proof">Merkle proof (list of sibling hashes)</param>
    /// <param name="hashType">Hash algorithm used</param>
    /// <returns>True if the data is part of the tree</returns>
    public bool VerifyMerkleProof(
        string dataHash,
        string merkleRoot,
        IReadOnlyList<string> proof,
        HashType hashType = HashType.SHA256)
    {
        if (string.IsNullOrWhiteSpace(dataHash) || string.IsNullOrWhiteSpace(merkleRoot))
            return false;

        string currentHash = dataHash.ToLowerInvariant();

        // Apply each proof element
        foreach (var proofElement in proof)
        {
            // Determine order (smaller hash goes first for deterministic ordering)
            if (string.Compare(currentHash, proofElement, StringComparison.OrdinalIgnoreCase) < 0)
            {
                currentHash = CombineAndHash(currentHash, proofElement, hashType);
            }
            else
            {
                currentHash = CombineAndHash(proofElement, currentHash, hashType);
            }
        }

        return string.Equals(currentHash, merkleRoot.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generates a Merkle inclusion proof for a single leaf by index.
    /// </summary>
    /// <param name="leafIndex">0-based position of the leaf in the transaction list</param>
    /// <param name="transactionHashes">All transaction hashes in the docket</param>
    /// <param name="hashType">Hash algorithm to use</param>
    /// <returns>Proof containing sibling hashes from leaf to root</returns>
    public MerkleProof GenerateInclusionProof(
        int leafIndex,
        IReadOnlyList<string> transactionHashes,
        HashType hashType = HashType.SHA256)
    {
        if (transactionHashes == null || transactionHashes.Count == 0)
            throw new ArgumentException("Transaction hashes cannot be empty", nameof(transactionHashes));
        if (leafIndex < 0 || leafIndex >= transactionHashes.Count)
            throw new ArgumentOutOfRangeException(nameof(leafIndex));

        var levels = BuildTreeLevels(transactionHashes, hashType);
        var proofSteps = ExtractProofPath(leafIndex, levels);
        var root = levels[^1][0];

        return new MerkleProof
        {
            TransactionHash = transactionHashes[leafIndex].ToLowerInvariant(),
            MerkleRoot = root,
            ProofPath = proofSteps,
            LeafIndex = leafIndex,
            TreeSize = transactionHashes.Count
        };
    }

    /// <summary>
    /// Generates Merkle inclusion proofs for all leaves in one O(n log n) pass.
    /// </summary>
    /// <param name="transactionHashes">All transaction hashes in the docket</param>
    /// <param name="hashType">Hash algorithm to use</param>
    /// <returns>Array of proofs, one per transaction, in the same order as the input</returns>
    public MerkleProof[] GenerateAllProofs(
        IReadOnlyList<string> transactionHashes,
        HashType hashType = HashType.SHA256)
    {
        if (transactionHashes == null || transactionHashes.Count == 0)
            return [];

        var levels = BuildTreeLevels(transactionHashes, hashType);
        var root = levels[^1][0];
        var proofs = new MerkleProof[transactionHashes.Count];

        for (int i = 0; i < transactionHashes.Count; i++)
        {
            var proofSteps = ExtractProofPath(i, levels);
            proofs[i] = new MerkleProof
            {
                TransactionHash = transactionHashes[i].ToLowerInvariant(),
                MerkleRoot = root,
                ProofPath = proofSteps,
                LeafIndex = i,
                TreeSize = transactionHashes.Count
            };
        }

        return proofs;
    }

    /// <summary>
    /// Builds all levels of the Merkle tree, retaining intermediate hashes.
    /// Level 0 = leaves, Level N = root (single element).
    /// </summary>
    private List<List<string>> BuildTreeLevels(IReadOnlyList<string> transactionHashes, HashType hashType)
    {
        var levels = new List<List<string>>();
        var currentLevel = transactionHashes.Select(h => h.ToLowerInvariant()).ToList();
        levels.Add(currentLevel);

        if (currentLevel.Count == 1)
            return levels;

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();

            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                string left = currentLevel[i];
                string right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;
                nextLevel.Add(CombineAndHash(left, right, hashType));
            }

            levels.Add(nextLevel);
            currentLevel = nextLevel;
        }

        return levels;
    }

    /// <summary>
    /// Extracts the sibling proof path for a specific leaf index from pre-built tree levels.
    /// </summary>
    private static List<MerkleProofStepData> ExtractProofPath(int leafIndex, List<List<string>> levels)
    {
        var steps = new List<MerkleProofStepData>();
        int index = leafIndex;

        // Walk from leaf level (0) up to the level below root
        for (int level = 0; level < levels.Count - 1; level++)
        {
            var currentLevel = levels[level];
            int siblingIndex = (index % 2 == 0) ? index + 1 : index - 1;

            // Handle odd-count duplication: if sibling is beyond the end, it's a duplicate of current
            if (siblingIndex >= currentLevel.Count)
                siblingIndex = index;

            var siblingHash = currentLevel[siblingIndex];
            var position = (index % 2 == 0)
                ? MerkleProofPosition.Right   // Current is left child, sibling is right
                : MerkleProofPosition.Left;   // Current is right child, sibling is left

            steps.Add(new MerkleProofStepData(siblingHash, position));

            // Move to parent index
            index /= 2;
        }

        return steps;
    }

    /// <summary>
    /// Combines two hashes and computes their hash
    /// </summary>
    private string CombineAndHash(string leftHash, string rightHash, HashType hashType)
    {
        // Concatenate the two hashes
        string combined = leftHash + rightHash;
        byte[] combinedBytes = Encoding.UTF8.GetBytes(combined);

        // Hash the combined string
        byte[] resultHash = _hashProvider.ComputeHash(combinedBytes, hashType);

        return Convert.ToHexString(resultHash).ToLowerInvariant();
    }
}

/// <summary>
/// Position of a sibling in a Merkle proof step.
/// </summary>
public enum MerkleProofPosition
{
    /// <summary>Sibling is to the left.</summary>
    Left = 0,
    /// <summary>Sibling is to the right.</summary>
    Right = 1
}

/// <summary>
/// A single step in a Merkle proof path (sibling hash and its position).
/// </summary>
/// <param name="Hash">Sibling hash at this tree level.</param>
/// <param name="Position">Whether the sibling is left or right of the current node.</param>
public record MerkleProofStepData(string Hash, MerkleProofPosition Position);

/// <summary>
/// A Merkle inclusion proof containing the sibling path from leaf to root.
/// </summary>
public class MerkleProof
{
    /// <summary>Hash of the proven transaction (leaf).</summary>
    public required string TransactionHash { get; init; }

    /// <summary>Merkle root of the tree.</summary>
    public required string MerkleRoot { get; init; }

    /// <summary>Sibling hashes from leaf to root.</summary>
    public required List<MerkleProofStepData> ProofPath { get; init; }

    /// <summary>0-based position of the leaf in the tree.</summary>
    public required int LeafIndex { get; init; }

    /// <summary>Total number of leaves in the tree.</summary>
    public required int TreeSize { get; init; }
}
