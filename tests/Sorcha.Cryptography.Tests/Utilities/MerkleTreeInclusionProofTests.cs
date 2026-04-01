// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using FluentAssertions;

using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;

using Xunit;

namespace Sorcha.Cryptography.Tests.Utilities;

/// <summary>
/// Tests for MerkleTree inclusion proof generation and verification.
/// Covers GenerateInclusionProof (T010) and GenerateAllProofs (T011).
/// Uses the real HashProvider to validate cryptographic correctness.
/// </summary>
public class MerkleTreeInclusionProofTests
{
    private readonly HashProvider _hashProvider;
    private readonly MerkleTree _merkleTree;

    public MerkleTreeInclusionProofTests()
    {
        _hashProvider = new HashProvider();
        _merkleTree = new MerkleTree(_hashProvider);
    }

    /// <summary>
    /// Generates deterministic hex-string hashes for test leaves.
    /// </summary>
    private List<string> GenerateHashes(int count)
    {
        var hashes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var data = Encoding.UTF8.GetBytes($"transaction-{i}");
            var hash = _hashProvider.ComputeHash(data, HashType.SHA256);
            hashes.Add(Convert.ToHexString(hash).ToLowerInvariant());
        }
        return hashes;
    }

    /// <summary>
    /// Walks a structured proof path from leaf to root, recomputing hashes at each level.
    /// Returns the computed root to compare against the expected Merkle root.
    /// </summary>
    private string RecomputeRootFromProof(MerkleProof proof)
    {
        var current = proof.TransactionHash;

        foreach (var step in proof.ProofPath)
        {
            string left, right;

            if (step.Position == MerkleProofPosition.Left)
            {
                // Sibling is on the left, current is on the right
                left = step.Hash;
                right = current;
            }
            else
            {
                // Sibling is on the right, current is on the left
                left = current;
                right = step.Hash;
            }

            var combined = Encoding.UTF8.GetBytes(left + right);
            var hash = _hashProvider.ComputeHash(combined, HashType.SHA256);
            current = Convert.ToHexString(hash).ToLowerInvariant();
        }

        return current;
    }

    #region GenerateInclusionProof Tests

    [Fact]
    public void GenerateInclusionProof_SingleLeaf_ReturnsEmptyProofPath()
    {
        // Arrange
        var hashes = GenerateHashes(1);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert
        proof.TransactionHash.Should().Be(hashes[0]);
        proof.MerkleRoot.Should().Be(hashes[0]);
        proof.ProofPath.Should().BeEmpty();
        proof.LeafIndex.Should().Be(0);
        proof.TreeSize.Should().Be(1);
    }

    [Fact]
    public void GenerateInclusionProof_TwoLeaves_Index0_ReturnsSiblingAsRight()
    {
        // Arrange
        var hashes = GenerateHashes(2);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert
        proof.TransactionHash.Should().Be(hashes[0]);
        proof.MerkleRoot.Should().Be(expectedRoot);
        proof.ProofPath.Should().HaveCount(1);
        proof.ProofPath[0].Hash.Should().Be(hashes[1]);
        proof.ProofPath[0].Position.Should().Be(MerkleProofPosition.Right);
        proof.LeafIndex.Should().Be(0);
        proof.TreeSize.Should().Be(2);

        // Verify the proof recomputes to the correct root
        RecomputeRootFromProof(proof).Should().Be(expectedRoot);
    }

    [Fact]
    public void GenerateInclusionProof_TwoLeaves_Index1_ReturnsSiblingAsLeft()
    {
        // Arrange
        var hashes = GenerateHashes(2);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(1, hashes);

        // Assert
        proof.TransactionHash.Should().Be(hashes[1]);
        proof.MerkleRoot.Should().Be(expectedRoot);
        proof.ProofPath.Should().HaveCount(1);
        proof.ProofPath[0].Hash.Should().Be(hashes[0]);
        proof.ProofPath[0].Position.Should().Be(MerkleProofPosition.Left);

        RecomputeRootFromProof(proof).Should().Be(expectedRoot);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void GenerateInclusionProof_OddLeafCount_AllIndicesVerify(int leafCount)
    {
        // Arrange
        var hashes = GenerateHashes(leafCount);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        for (int i = 0; i < leafCount; i++)
        {
            // Act
            var proof = _merkleTree.GenerateInclusionProof(i, hashes);

            // Assert
            proof.TransactionHash.Should().Be(hashes[i]);
            proof.MerkleRoot.Should().Be(expectedRoot);
            proof.LeafIndex.Should().Be(i);
            proof.TreeSize.Should().Be(leafCount);
            RecomputeRootFromProof(proof).Should().Be(expectedRoot,
                $"proof for leaf index {i} in tree of {leafCount} should recompute to root");
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void GenerateInclusionProof_PowerOfTwoLeafCount_AllIndicesVerify(int leafCount)
    {
        // Arrange
        var hashes = GenerateHashes(leafCount);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        for (int i = 0; i < leafCount; i++)
        {
            // Act
            var proof = _merkleTree.GenerateInclusionProof(i, hashes);

            // Assert
            proof.MerkleRoot.Should().Be(expectedRoot);
            RecomputeRootFromProof(proof).Should().Be(expectedRoot,
                $"proof for leaf index {i} in tree of {leafCount} should recompute to root");
        }
    }

    [Fact]
    public void GenerateInclusionProof_LargeTree_AllProofsVerify()
    {
        // Arrange - 128 transactions
        var hashes = GenerateHashes(128);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act & Assert - verify first, last, and several middle indices
        foreach (var index in new[] { 0, 1, 63, 64, 100, 127 })
        {
            var proof = _merkleTree.GenerateInclusionProof(index, hashes);

            proof.MerkleRoot.Should().Be(expectedRoot);
            proof.LeafIndex.Should().Be(index);
            proof.TreeSize.Should().Be(128);
            RecomputeRootFromProof(proof).Should().Be(expectedRoot,
                $"proof for leaf index {index} in 128-leaf tree should verify");
        }
    }

    [Fact]
    public void GenerateInclusionProof_LargeTree_ProofPathDepthIsLogN()
    {
        // Arrange - 128 = 2^7 leaves => depth 7
        var hashes = GenerateHashes(128);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert - proof depth should be ceil(log2(n))
        proof.ProofPath.Should().HaveCount(7);
    }

    [Fact]
    public void GenerateInclusionProof_BoundaryIndex0_Verifies()
    {
        // Arrange
        var hashes = GenerateHashes(10);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert
        proof.LeafIndex.Should().Be(0);
        proof.TransactionHash.Should().Be(hashes[0]);
        RecomputeRootFromProof(proof).Should().Be(expectedRoot);
    }

    [Fact]
    public void GenerateInclusionProof_BoundaryLastIndex_Verifies()
    {
        // Arrange
        var hashes = GenerateHashes(10);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(9, hashes);

        // Assert
        proof.LeafIndex.Should().Be(9);
        proof.TransactionHash.Should().Be(hashes[9]);
        RecomputeRootFromProof(proof).Should().Be(expectedRoot);
    }

    [Fact]
    public void GenerateInclusionProof_NegativeIndex_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var hashes = GenerateHashes(5);

        // Act
        var act = () => _merkleTree.GenerateInclusionProof(-1, hashes);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("leafIndex");
    }

    [Fact]
    public void GenerateInclusionProof_IndexEqualToCount_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var hashes = GenerateHashes(5);

        // Act
        var act = () => _merkleTree.GenerateInclusionProof(5, hashes);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("leafIndex");
    }

    [Fact]
    public void GenerateInclusionProof_IndexBeyondCount_ThrowsArgumentOutOfRange()
    {
        // Arrange
        var hashes = GenerateHashes(3);

        // Act
        var act = () => _merkleTree.GenerateInclusionProof(10, hashes);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GenerateInclusionProof_EmptyList_ThrowsArgumentException()
    {
        // Arrange
        var hashes = new List<string>();

        // Act
        var act = () => _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateInclusionProof_NullList_ThrowsArgumentException()
    {
        // Act
        var act = () => _merkleTree.GenerateInclusionProof(0, null!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateInclusionProof_StructuredProofRecomputesRoot_AllIndices()
    {
        // Arrange - verify that the structured proof (with positional data)
        // correctly recomputes the root for every leaf in an 8-leaf tree
        var hashes = GenerateHashes(8);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        for (int i = 0; i < hashes.Count; i++)
        {
            // Act
            var proof = _merkleTree.GenerateInclusionProof(i, hashes);

            // Assert - walk the proof path using positional information
            RecomputeRootFromProof(proof).Should().Be(expectedRoot,
                $"structured proof for leaf {i} should recompute to root");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(15)]
    public void GenerateInclusionProof_StructuredVerification_AllSizes(int leafCount)
    {
        // Arrange
        var hashes = GenerateHashes(leafCount);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        for (int i = 0; i < leafCount; i++)
        {
            // Act
            var proof = _merkleTree.GenerateInclusionProof(i, hashes);

            // Assert - the proof path with positional data correctly recomputes the root
            RecomputeRootFromProof(proof).Should().Be(expectedRoot,
                $"leaf index {i} in {leafCount}-leaf tree should verify via structured proof");
        }
    }

    [Fact]
    public void GenerateInclusionProof_SingleLeaf_VerifyMerkleProofWithEmptyPath()
    {
        // Arrange - single leaf: proof path is empty, VerifyMerkleProof with empty
        // proof compares dataHash directly against root
        var hashes = GenerateHashes(1);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);
        var flatProof = proof.ProofPath.Select(s => s.Hash).ToList();

        // Assert
        _merkleTree.VerifyMerkleProof(proof.TransactionHash, expectedRoot, flatProof)
            .Should().BeTrue("single leaf proof should verify via VerifyMerkleProof");
    }

    [Fact]
    public void GenerateInclusionProof_MixedCaseInput_NormalizesHashes()
    {
        // Arrange
        var hashes = new List<string> { "ABCDEF0123456789", "abcdef0123456789" };
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proof = _merkleTree.GenerateInclusionProof(0, hashes);

        // Assert
        proof.TransactionHash.Should().Be("abcdef0123456789");
        proof.MerkleRoot.Should().Be(expectedRoot);
        RecomputeRootFromProof(proof).Should().Be(expectedRoot);
    }

    [Fact]
    public void GenerateInclusionProof_ConsistentAcrossMultipleCalls()
    {
        // Arrange
        var hashes = GenerateHashes(10);

        // Act
        var proof1 = _merkleTree.GenerateInclusionProof(5, hashes);
        var proof2 = _merkleTree.GenerateInclusionProof(5, hashes);

        // Assert
        proof1.TransactionHash.Should().Be(proof2.TransactionHash);
        proof1.MerkleRoot.Should().Be(proof2.MerkleRoot);
        proof1.LeafIndex.Should().Be(proof2.LeafIndex);
        proof1.TreeSize.Should().Be(proof2.TreeSize);
        proof1.ProofPath.Should().HaveCount(proof2.ProofPath.Count);

        for (int i = 0; i < proof1.ProofPath.Count; i++)
        {
            proof1.ProofPath[i].Hash.Should().Be(proof2.ProofPath[i].Hash);
            proof1.ProofPath[i].Position.Should().Be(proof2.ProofPath[i].Position);
        }
    }

    #endregion

    #region GenerateAllProofs Tests

    [Fact]
    public void GenerateAllProofs_EmptyList_ReturnsEmptyArray()
    {
        // Arrange
        var hashes = new List<string>();

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert
        proofs.Should().BeEmpty();
    }

    [Fact]
    public void GenerateAllProofs_NullList_ReturnsEmptyArray()
    {
        // Act
        var proofs = _merkleTree.GenerateAllProofs(null!);

        // Assert
        proofs.Should().BeEmpty();
    }

    [Fact]
    public void GenerateAllProofs_SingleLeaf_ReturnsSingleProof()
    {
        // Arrange
        var hashes = GenerateHashes(1);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert
        proofs.Should().HaveCount(1);
        proofs[0].TransactionHash.Should().Be(hashes[0]);
        proofs[0].MerkleRoot.Should().Be(hashes[0]);
        proofs[0].ProofPath.Should().BeEmpty();
        proofs[0].LeafIndex.Should().Be(0);
        proofs[0].TreeSize.Should().Be(1);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    public void GenerateAllProofs_AllProofsVerifyAgainstRoot(int leafCount)
    {
        // Arrange
        var hashes = GenerateHashes(leafCount);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert
        proofs.Should().HaveCount(leafCount);

        for (int i = 0; i < leafCount; i++)
        {
            proofs[i].TransactionHash.Should().Be(hashes[i]);
            proofs[i].MerkleRoot.Should().Be(expectedRoot);
            proofs[i].LeafIndex.Should().Be(i);
            proofs[i].TreeSize.Should().Be(leafCount);
            RecomputeRootFromProof(proofs[i]).Should().Be(expectedRoot,
                $"proof for leaf {i} in {leafCount}-leaf tree should recompute to root");
        }
    }

    [Fact]
    public void GenerateAllProofs_AllShareSameRoot()
    {
        // Arrange
        var hashes = GenerateHashes(10);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert
        var roots = proofs.Select(p => p.MerkleRoot).Distinct().ToList();
        roots.Should().HaveCount(1, "all proofs in the same tree must share the same root");
    }

    [Fact]
    public void GenerateAllProofs_MatchesIndividualProofs()
    {
        // Arrange
        var hashes = GenerateHashes(7);

        // Act
        var allProofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert - each proof from GenerateAllProofs should match GenerateInclusionProof
        for (int i = 0; i < hashes.Count; i++)
        {
            var individual = _merkleTree.GenerateInclusionProof(i, hashes);

            allProofs[i].TransactionHash.Should().Be(individual.TransactionHash);
            allProofs[i].MerkleRoot.Should().Be(individual.MerkleRoot);
            allProofs[i].LeafIndex.Should().Be(individual.LeafIndex);
            allProofs[i].TreeSize.Should().Be(individual.TreeSize);
            allProofs[i].ProofPath.Should().HaveCount(individual.ProofPath.Count);

            for (int j = 0; j < individual.ProofPath.Count; j++)
            {
                allProofs[i].ProofPath[j].Hash.Should().Be(individual.ProofPath[j].Hash);
                allProofs[i].ProofPath[j].Position.Should().Be(individual.ProofPath[j].Position);
            }
        }
    }

    [Fact]
    public void GenerateAllProofs_StructuredVerification_AllRecomputeToRoot()
    {
        // Arrange
        var hashes = GenerateHashes(12);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert - every proof should recompute to the root via positional walk
        for (int i = 0; i < hashes.Count; i++)
        {
            RecomputeRootFromProof(proofs[i]).Should().Be(expectedRoot,
                $"proof for leaf {i} should recompute to root");
        }
    }

    [Fact]
    public void GenerateAllProofs_LargeTree_AllProofsVerify()
    {
        // Arrange - 150 transactions
        var hashes = GenerateHashes(150);
        var expectedRoot = _merkleTree.ComputeMerkleRoot(hashes);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert
        proofs.Should().HaveCount(150);

        foreach (var proof in proofs)
        {
            proof.MerkleRoot.Should().Be(expectedRoot);
            RecomputeRootFromProof(proof).Should().Be(expectedRoot);
        }
    }

    [Fact]
    public void GenerateAllProofs_ProofPathDepthConsistent()
    {
        // Arrange - 16 leaves => balanced tree, depth 4 for all
        var hashes = GenerateHashes(16);

        // Act
        var proofs = _merkleTree.GenerateAllProofs(hashes);

        // Assert - all proofs in a power-of-2 tree should have the same depth
        foreach (var proof in proofs)
        {
            proof.ProofPath.Should().HaveCount(4,
                $"16-leaf balanced tree should produce depth-4 proofs (leaf {proof.LeafIndex})");
        }
    }

    #endregion
}
