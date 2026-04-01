// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// Position of a sibling hash in the Merkle proof path.
/// </summary>
public enum ProofPosition
{
    /// <summary>Sibling is to the left of the current node.</summary>
    Left = 0,

    /// <summary>Sibling is to the right of the current node.</summary>
    Right = 1
}

/// <summary>
/// A single step in a Merkle inclusion proof path.
/// </summary>
public record MerkleProofStep
{
    /// <summary>Sibling hash at this tree level.</summary>
    public required string Hash { get; init; }

    /// <summary>Whether the sibling is to the left or right.</summary>
    public required ProofPosition Position { get; init; }
}

/// <summary>
/// A compact proof that a transaction hash is a leaf in a docket's Merkle tree.
/// Contains the sibling hashes from leaf to root (log2(n) steps).
/// </summary>
public record MerkleInclusionProof
{
    /// <summary>SHA-256 hash of the proven transaction.</summary>
    public required string TransactionHash { get; init; }

    /// <summary>Docket height where the transaction was sealed.</summary>
    public required long DocketNumber { get; init; }

    /// <summary>Expected Merkle root hash of the docket.</summary>
    public required string MerkleRoot { get; init; }

    /// <summary>Sibling hashes from leaf to root.</summary>
    public required IReadOnlyList<MerkleProofStep> ProofPath { get; init; }

    /// <summary>Position of the transaction in the tree (0-based).</summary>
    public required int LeafIndex { get; init; }

    /// <summary>Total number of leaves (transactions) in the docket.</summary>
    public required int TreeSize { get; init; }
}
