// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for MerkleTree
/// Tests cover Merkle root computation, determinism, edge cases, and proof verification
/// </summary>
public class MerkleTreeTests
{
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly MerkleTree _merkleTree;

    public MerkleTreeTests()
    {
        _mockHashProvider = new Mock<IHashProvider>();

        // Setup hash provider to return deterministic SHA256-like hashes
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType hashType) =>
            {
                // Use real SHA256 for deterministic test behavior
                return SHA256.HashData(data);
            });

        _merkleTree = new MerkleTree(_mockHashProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHashProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new MerkleTree(null!));
        exception.ParamName.Should().Be("hashProvider");
    }

    #endregion

    #region ComputeMerkleRoot - Empty/Null Input Tests

    [Fact]
    public void ComputeMerkleRoot_WithNullList_ReturnsHashOfEmptyBytes()
    {
        // Act
        var result = _merkleTree.ComputeMerkleRoot(null!);

        // Assert
        result.Should().NotBeNullOrEmpty();
        _mockHashProvider.Verify(
            h => h.ComputeHash(Array.Empty<byte>(), HashType.SHA256),
            Times.Once);
    }

    [Fact]
    public void ComputeMerkleRoot_WithEmptyList_ReturnsHashOfEmptyBytes()
    {
        // Arrange
        var emptyList = new List<string>();

        // Act
        var result = _merkleTree.ComputeMerkleRoot(emptyList);

        // Assert
        result.Should().NotBeNullOrEmpty();
        _mockHashProvider.Verify(
            h => h.ComputeHash(Array.Empty<byte>(), HashType.SHA256),
            Times.Once);
    }

    [Fact]
    public void ComputeMerkleRoot_WithNullAndEmptyList_ProduceSameResult()
    {
        // Act
        var nullResult = _merkleTree.ComputeMerkleRoot(null!);
        var emptyResult = _merkleTree.ComputeMerkleRoot(new List<string>());

        // Assert
        nullResult.Should().Be(emptyResult);
    }

    #endregion

    #region ComputeMerkleRoot - Single Transaction Tests

    [Fact]
    public void ComputeMerkleRoot_WithSingleTransaction_ReturnsThatHash()
    {
        // Arrange
        var hash = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890";
        var hashes = new List<string> { hash };

        // Act
        var result = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result.Should().Be(hash);
    }

    [Fact]
    public void ComputeMerkleRoot_WithSingleTransaction_NormalizesToLowerCase()
    {
        // Arrange
        var hash = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890";
        var hashes = new List<string> { hash };

        // Act
        var result = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result.Should().Be(hash.ToLowerInvariant());
    }

    #endregion

    #region ComputeMerkleRoot - Two Transaction Tests

    [Fact]
    public void ComputeMerkleRoot_WithTwoTransactions_CombinesAndHashes()
    {
        // Arrange
        var hash1 = "aaaa";
        var hash2 = "bbbb";
        var hashes = new List<string> { hash1, hash2 };

        // Act
        var result = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().NotBe(hash1);
        result.Should().NotBe(hash2);

        // The hash provider should be called to combine the two hashes
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256),
            Times.Once); // One combine-and-hash call for the pair
    }

    [Fact]
    public void ComputeMerkleRoot_WithTwoTransactions_OrderMatters()
    {
        // Arrange
        var hash1 = "aaaa";
        var hash2 = "bbbb";

        // Act
        var result1 = _merkleTree.ComputeMerkleRoot(new List<string> { hash1, hash2 });
        var result2 = _merkleTree.ComputeMerkleRoot(new List<string> { hash2, hash1 });

        // Assert - different order should produce different root
        result1.Should().NotBe(result2);
    }

    #endregion

    #region ComputeMerkleRoot - Odd Number of Transactions Tests

    [Fact]
    public void ComputeMerkleRoot_WithThreeTransactions_HandlesOddCount()
    {
        // Arrange
        var hashes = new List<string> { "aaaa", "bbbb", "cccc" };

        // Act
        var result = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // With 3 items: (aaaa+bbbb) and (cccc+cccc) at level 1, then combine at level 0
    }

    [Fact]
    public void ComputeMerkleRoot_WithOddCount_DuplicatesLastElement()
    {
        // Arrange - 3 hashes where last is duplicated
        var hash1 = "aaaa";
        var hash2 = "bbbb";
        var hash3 = "cccc";

        // With 3 items: pairs are (aaaa,bbbb) and (cccc,cccc)
        // With 4 items where last two are same: pairs are (aaaa,bbbb) and (cccc,cccc)
        var threeHashes = new List<string> { hash1, hash2, hash3 };
        var fourHashesWithDup = new List<string> { hash1, hash2, hash3, hash3 };

        // Act
        var resultThree = _merkleTree.ComputeMerkleRoot(threeHashes);
        var resultFour = _merkleTree.ComputeMerkleRoot(fourHashesWithDup);

        // Assert - odd count duplicates last, so 3-item tree should equal 4-item with dup
        resultThree.Should().Be(resultFour);
    }

    #endregion

    #region ComputeMerkleRoot - Large Batch Tests

    [Fact]
    public void ComputeMerkleRoot_WithLargeBatch_ProducesDeterministicRoot()
    {
        // Arrange
        var hashes = Enumerable.Range(0, 100)
            .Select(i => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"tx-{i}"))).ToLowerInvariant())
            .ToList();

        // Act
        var result1 = _merkleTree.ComputeMerkleRoot(hashes);
        var result2 = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result1.Should().NotBeNullOrEmpty();
        result1.Should().Be(result2);
    }

    [Fact]
    public void ComputeMerkleRoot_WithPowerOfTwoCount_ProducesDeterministicRoot()
    {
        // Arrange - 8 transactions (perfect binary tree)
        var hashes = Enumerable.Range(0, 8)
            .Select(i => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"tx-{i}"))).ToLowerInvariant())
            .ToList();

        // Act
        var result1 = _merkleTree.ComputeMerkleRoot(hashes);
        var result2 = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result1.Should().Be(result2);
    }

    #endregion

    #region ComputeMerkleRoot - Determinism Tests

    [Fact]
    public void ComputeMerkleRoot_SameInputs_ProduceSameOutput()
    {
        // Arrange
        var hashes = new List<string> { "aaaa", "bbbb", "cccc", "dddd" };

        // Act
        var result1 = _merkleTree.ComputeMerkleRoot(hashes);
        var result2 = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void ComputeMerkleRoot_DifferentInputs_ProduceDifferentOutput()
    {
        // Arrange
        var hashes1 = new List<string> { "aaaa", "bbbb", "cccc", "dddd" };
        var hashes2 = new List<string> { "aaaa", "bbbb", "cccc", "eeee" };

        // Act
        var result1 = _merkleTree.ComputeMerkleRoot(hashes1);
        var result2 = _merkleTree.ComputeMerkleRoot(hashes2);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void ComputeMerkleRoot_CaseInsensitiveInput_ProducesSameOutput()
    {
        // Arrange
        var hashesLower = new List<string> { "abcd", "ef01" };
        var hashesUpper = new List<string> { "ABCD", "EF01" };

        // Act
        var resultLower = _merkleTree.ComputeMerkleRoot(hashesLower);
        var resultUpper = _merkleTree.ComputeMerkleRoot(hashesUpper);

        // Assert - should normalize to lowercase
        resultLower.Should().Be(resultUpper);
    }

    #endregion

    #region ComputeMerkleRoot - HashType Tests

    [Fact]
    public void ComputeMerkleRoot_WithSHA512HashType_PassesHashTypeToProvider()
    {
        // Arrange
        var hashes = new List<string> { "aaaa", "bbbb" };

        // Act
        _merkleTree.ComputeMerkleRoot(hashes, HashType.SHA512);

        // Assert
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA512),
            Times.AtLeastOnce);
    }

    [Fact]
    public void ComputeMerkleRoot_DefaultHashType_UsesSHA256()
    {
        // Arrange
        var hashes = new List<string> { "aaaa", "bbbb" };

        // Act
        _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256),
            Times.AtLeastOnce);
    }

    #endregion

    #region ComputeMerkleRootFromData Tests

    [Fact]
    public void ComputeMerkleRootFromData_WithNullList_ReturnsHashOfEmptyBytes()
    {
        // Act
        var result = _merkleTree.ComputeMerkleRootFromData(null!);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeMerkleRootFromData_WithEmptyList_ReturnsHashOfEmptyBytes()
    {
        // Arrange
        var emptyList = new List<byte[]>();

        // Act
        var result = _merkleTree.ComputeMerkleRootFromData(emptyList);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ComputeMerkleRootFromData_WithSingleItem_HashesDataThenReturnsLeafHash()
    {
        // Arrange
        var data = new List<byte[]> { Encoding.UTF8.GetBytes("transaction-data") };

        // Act
        var result = _merkleTree.ComputeMerkleRootFromData(data);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should hash the data item first, then return it as the single leaf
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256),
            Times.Once); // One call to hash the data item
    }

    [Fact]
    public void ComputeMerkleRootFromData_WithMultipleItems_ProducesDeterministicResult()
    {
        // Arrange
        var data = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("tx-1"),
            Encoding.UTF8.GetBytes("tx-2"),
            Encoding.UTF8.GetBytes("tx-3")
        };

        // Act
        var result1 = _merkleTree.ComputeMerkleRootFromData(data);
        var result2 = _merkleTree.ComputeMerkleRootFromData(data);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void ComputeMerkleRootFromData_DifferentData_ProducesDifferentRoots()
    {
        // Arrange
        var data1 = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("tx-1"),
            Encoding.UTF8.GetBytes("tx-2")
        };
        var data2 = new List<byte[]>
        {
            Encoding.UTF8.GetBytes("tx-1"),
            Encoding.UTF8.GetBytes("tx-3")
        };

        // Act
        var result1 = _merkleTree.ComputeMerkleRootFromData(data1);
        var result2 = _merkleTree.ComputeMerkleRootFromData(data2);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void ComputeMerkleRootFromData_WithSHA512_PassesHashTypeToProvider()
    {
        // Arrange
        var data = new List<byte[]> { Encoding.UTF8.GetBytes("tx-1") };

        // Act
        _merkleTree.ComputeMerkleRootFromData(data, HashType.SHA512);

        // Assert
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA512),
            Times.AtLeastOnce);
    }

    #endregion

    #region VerifyMerkleProof Tests

    [Fact]
    public void VerifyMerkleProof_WithNullDataHash_ReturnsFalse()
    {
        // Act
        var result = _merkleTree.VerifyMerkleProof(null!, "root", new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithEmptyDataHash_ReturnsFalse()
    {
        // Act
        var result = _merkleTree.VerifyMerkleProof("", "root", new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithWhitespaceDataHash_ReturnsFalse()
    {
        // Act
        var result = _merkleTree.VerifyMerkleProof("   ", "root", new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithNullMerkleRoot_ReturnsFalse()
    {
        // Act
        var result = _merkleTree.VerifyMerkleProof("datahash", null!, new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithEmptyMerkleRoot_ReturnsFalse()
    {
        // Act
        var result = _merkleTree.VerifyMerkleProof("datahash", "", new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithEmptyProofAndMatchingHash_ReturnsTrue()
    {
        // Arrange - single-element tree where data hash IS the root
        var dataHash = "abcdef1234";
        var merkleRoot = "abcdef1234";

        // Act
        var result = _merkleTree.VerifyMerkleProof(dataHash, merkleRoot, new List<string>());

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyMerkleProof_WithEmptyProofAndNonMatchingHash_ReturnsFalse()
    {
        // Arrange
        var dataHash = "abcdef1234";
        var merkleRoot = "ffffffff9999";

        // Act
        var result = _merkleTree.VerifyMerkleProof(dataHash, merkleRoot, new List<string>());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_CaseInsensitiveComparison()
    {
        // Arrange
        var dataHash = "ABCDEF";
        var merkleRoot = "abcdef";

        // Act
        var result = _merkleTree.VerifyMerkleProof(dataHash, merkleRoot, new List<string>());

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyMerkleProof_WithWrongProof_ReturnsFalse()
    {
        // Arrange
        var dataHash = "aaaa";
        var wrongProof = new List<string> { "xxxx", "yyyy" };
        var fakeMerkleRoot = "not-the-real-root";

        // Act
        var result = _merkleTree.VerifyMerkleProof(dataHash, fakeMerkleRoot, wrongProof);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyMerkleProof_WithProofElements_AppliesHashesSequentially()
    {
        // Arrange - verify the proof mechanism doesn't throw and processes elements
        var dataHash = "aaaa";
        var proof = new List<string> { "bbbb", "cccc" };

        // Act - should not throw
        var act = () => _merkleTree.VerifyMerkleProof(dataHash, "somehash", proof);

        // Assert
        act.Should().NotThrow();
        _mockHashProvider.Verify(
            h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256),
            Times.Exactly(2)); // One hash per proof element
    }

    #endregion

    #region Output Format Tests

    [Fact]
    public void ComputeMerkleRoot_OutputIsLowerCaseHex()
    {
        // Arrange
        var hashes = new List<string> { "aaaa", "bbbb" };

        // Act
        var result = _merkleTree.ComputeMerkleRoot(hashes);

        // Assert
        result.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void ComputeMerkleRoot_EmptyInput_OutputIsLowerCaseHex()
    {
        // Act
        var result = _merkleTree.ComputeMerkleRoot(new List<string>());

        // Assert
        result.Should().MatchRegex("^[0-9a-f]+$");
    }

    #endregion
}
