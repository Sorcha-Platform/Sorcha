// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Core;

/// <summary>
/// Result of inclusion proof verification.
/// </summary>
public record InclusionProofValidationResult
{
    public required bool IsValid { get; init; }
    public required string ComputedRoot { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Validates Merkle inclusion proofs using positional verification.
/// Recomputes the Merkle root by walking the proof path from leaf to root
/// using the Left/Right positions from each proof step.
/// Portable/enclave-safe: no network or database access.
/// </summary>
public class InclusionProofValidator
{
    private readonly IHashProvider _hashProvider;

    public InclusionProofValidator(IHashProvider hashProvider)
    {
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <summary>
    /// Verifies that a Merkle inclusion proof recomputes to the expected root.
    /// Uses positional verification (Left/Right from proof steps), NOT lexicographic ordering.
    /// </summary>
    public InclusionProofValidationResult Verify(MerkleInclusionProof proof)
    {
        if (proof == null)
            return new InclusionProofValidationResult
            {
                IsValid = false,
                ComputedRoot = string.Empty,
                Error = "Proof is null"
            };

        return VerifyPositional(proof.TransactionHash, proof.MerkleRoot, proof.ProofPath);
    }

    /// <summary>
    /// Verifies an inclusion proof against an explicitly provided Merkle root.
    /// </summary>
    public InclusionProofValidationResult Verify(
        string transactionHash,
        string expectedMerkleRoot,
        IReadOnlyList<MerkleProofStep> proofPath)
    {
        return VerifyPositional(transactionHash, expectedMerkleRoot, proofPath);
    }

    /// <summary>
    /// Positional proof verification: walks proof path using Left/Right positions
    /// to recompute the Merkle root. This matches how ComputeMerkleRoot builds the tree.
    /// </summary>
    private InclusionProofValidationResult VerifyPositional(
        string transactionHash,
        string expectedMerkleRoot,
        IReadOnlyList<MerkleProofStep> proofPath)
    {
        var currentHash = transactionHash.ToLowerInvariant();

        foreach (var step in proofPath)
        {
            // Position indicates where the SIBLING is, so:
            // - Left sibling: sibling is left, current is right → hash(sibling + current)
            // - Right sibling: sibling is right, current is left → hash(current + sibling)
            string left, right;
            if (step.Position == ProofPosition.Left)
            {
                left = step.Hash.ToLowerInvariant();
                right = currentHash;
            }
            else
            {
                left = currentHash;
                right = step.Hash.ToLowerInvariant();
            }

            currentHash = CombineAndHash(left, right);
        }

        var isValid = string.Equals(currentHash, expectedMerkleRoot.ToLowerInvariant(),
            StringComparison.OrdinalIgnoreCase);

        return new InclusionProofValidationResult
        {
            IsValid = isValid,
            ComputedRoot = currentHash,
            Error = isValid ? null : $"Computed root {currentHash} does not match expected {expectedMerkleRoot}"
        };
    }

    /// <summary>
    /// Combines two hashes using the same method as MerkleTree.
    /// </summary>
    private string CombineAndHash(string leftHash, string rightHash)
    {
        var combined = leftHash + rightHash;
        var combinedBytes = Encoding.UTF8.GetBytes(combined);
        var resultHash = _hashProvider.ComputeHash(combinedBytes, HashType.SHA256);
        return Convert.ToHexString(resultHash).ToLowerInvariant();
    }
}
