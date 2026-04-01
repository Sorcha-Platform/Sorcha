// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Validator.Core;
using Xunit;

namespace Sorcha.Validator.Core.Tests;

/// <summary>
/// Unit tests for InclusionProofValidator (T030).
/// Validates positional Merkle inclusion proof verification.
/// </summary>
public class InclusionProofValidatorTests
{
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly InclusionProofValidator _validator;

    public InclusionProofValidatorTests()
    {
        _mockHashProvider = new Mock<IHashProvider>();

        // Use real SHA256 for deterministic hash computation
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) => SHA256.HashData(data));

        _validator = new InclusionProofValidator(_mockHashProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHashProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new InclusionProofValidator(null!));
        exception.ParamName.Should().Be("hashProvider");
    }

    #endregion

    #region Verify - Valid Proof Tests

    [Fact]
    public void Verify_ValidProofWithTwoLeaves_ReturnsValid()
    {
        // Arrange: Build a 2-leaf Merkle tree manually
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        // Proof for leafA: sibling is leafB on the right
        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafA,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = leafB, Position = ProofPosition.Right }
            },
            LeafIndex = 0,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ComputedRoot.Should().Be(root);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Verify_ValidProofForSecondLeaf_ReturnsValid()
    {
        // Arrange: Build a 2-leaf Merkle tree
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        // Proof for leafB: sibling is leafA on the left
        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafB,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = leafA, Position = ProofPosition.Left }
            },
            LeafIndex = 1,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ComputedRoot.Should().Be(root);
    }

    [Fact]
    public void Verify_ValidProofWithFourLeaves_ReturnsValid()
    {
        // Arrange: Build a 4-leaf Merkle tree
        //       root
        //      /    \
        //    h01     h23
        //   / \     / \
        //  L0  L1  L2  L3
        var leaf0 = ComputeLeafHash("leaf-0");
        var leaf1 = ComputeLeafHash("leaf-1");
        var leaf2 = ComputeLeafHash("leaf-2");
        var leaf3 = ComputeLeafHash("leaf-3");

        var h01 = CombineAndHash(leaf0, leaf1);
        var h23 = CombineAndHash(leaf2, leaf3);
        var root = CombineAndHash(h01, h23);

        // Proof for leaf2 (index 2): sibling leaf3 (Right), then h01 (Left)
        var proof = new MerkleInclusionProof
        {
            TransactionHash = leaf2,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = leaf3, Position = ProofPosition.Right },
                new() { Hash = h01, Position = ProofPosition.Left }
            },
            LeafIndex = 2,
            TreeSize = 4
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ComputedRoot.Should().Be(root);
    }

    [Fact]
    public void Verify_EmptyProofPath_SingleLeafDocket_ReturnsValid()
    {
        // Arrange: Single-leaf tree — the leaf IS the root, so proof path is empty
        var leafHash = ComputeLeafHash("only-transaction");

        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafHash,
            DocketNumber = 1,
            MerkleRoot = leafHash,
            ProofPath = new List<MerkleProofStep>(),
            LeafIndex = 0,
            TreeSize = 1
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ComputedRoot.Should().Be(leafHash);
    }

    #endregion

    #region Verify - Invalid Proof Tests

    [Fact]
    public void Verify_NullProof_ReturnsInvalid()
    {
        // Act
        var result = _validator.Verify((MerkleInclusionProof)null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Proof is null");
    }

    [Fact]
    public void Verify_TamperedTransactionHash_ReturnsInvalid()
    {
        // Arrange: Build a valid 2-leaf tree but tamper with the leaf hash
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        var tamperedHash = ComputeLeafHash("tampered-hash");

        var proof = new MerkleInclusionProof
        {
            TransactionHash = tamperedHash,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = leafB, Position = ProofPosition.Right }
            },
            LeafIndex = 0,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("does not match expected");
    }

    [Fact]
    public void Verify_WrongMerkleRoot_ReturnsInvalid()
    {
        // Arrange: Valid proof path but wrong expected root
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var wrongRoot = ComputeLeafHash("wrong-root");

        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafA,
            DocketNumber = 1,
            MerkleRoot = wrongRoot,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = leafB, Position = ProofPosition.Right }
            },
            LeafIndex = 0,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("does not match expected");
    }

    [Fact]
    public void Verify_TamperedProofStepHash_ReturnsInvalid()
    {
        // Arrange: Valid tree but tamper with a sibling hash in the proof path
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        var tamperedSibling = ComputeLeafHash("tampered-sibling");

        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafA,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                new() { Hash = tamperedSibling, Position = ProofPosition.Right }
            },
            LeafIndex = 0,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ComputedRoot.Should().NotBe(root);
    }

    [Fact]
    public void Verify_SwappedPosition_ReturnsInvalid()
    {
        // Arrange: Correct sibling hash but wrong position (Left instead of Right)
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        var proof = new MerkleInclusionProof
        {
            TransactionHash = leafA,
            DocketNumber = 1,
            MerkleRoot = root,
            ProofPath = new List<MerkleProofStep>
            {
                // leafB should be Right (since leafA is Left child), but we say Left
                new() { Hash = leafB, Position = ProofPosition.Left }
            },
            LeafIndex = 0,
            TreeSize = 2
        };

        // Act
        var result = _validator.Verify(proof);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region Verify Overload (explicit parameters) Tests

    [Fact]
    public void Verify_ExplicitParameters_ValidProof_ReturnsValid()
    {
        // Arrange
        var leafA = ComputeLeafHash("tx-hash-a");
        var leafB = ComputeLeafHash("tx-hash-b");
        var root = CombineAndHash(leafA, leafB);

        var proofPath = new List<MerkleProofStep>
        {
            new() { Hash = leafB, Position = ProofPosition.Right }
        };

        // Act
        var result = _validator.Verify(leafA, root, proofPath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ComputedRoot.Should().Be(root);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Computes a leaf hash by lowercasing the input (simulating the tree's normalization).
    /// For tests we use the value directly as a hex-like string since the validator
    /// treats TransactionHash as an already-computed hex hash.
    /// </summary>
    private string ComputeLeafHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Combines two hashes (left + right concatenation) and hashes the result,
    /// matching the InclusionProofValidator's CombineAndHash method.
    /// </summary>
    private string CombineAndHash(string leftHash, string rightHash)
    {
        var combined = leftHash + rightHash;
        var combinedBytes = Encoding.UTF8.GetBytes(combined);
        var resultHash = SHA256.HashData(combinedBytes);
        return Convert.ToHexString(resultHash).ToLowerInvariant();
    }

    #endregion
}
