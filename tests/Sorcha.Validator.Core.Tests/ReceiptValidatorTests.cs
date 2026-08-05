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
/// Unit tests for ReceiptValidator (T025).
/// Validates receipt signature verification and inclusion proof checking.
/// </summary>
public class ReceiptValidatorTests
{
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly InclusionProofValidator _proofValidator;

    public ReceiptValidatorTests()
    {
        _mockHashProvider = new Mock<IHashProvider>();
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) => SHA256.HashData(data));

        _proofValidator = new InclusionProofValidator(_mockHashProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithSignatureFunc_NullProofValidator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ReceiptValidator(null!, (_, _, _, _) => true));
        ex.ParamName.Should().Be("proofValidator");
    }

    [Fact]
    public void Constructor_WithSignatureFunc_NullVerifyFunc_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ReceiptValidator(_proofValidator, (SignatureVerifyFunc)null!));
        ex.ParamName.Should().Be("verifySignature");
    }

    [Fact]
    public void Constructor_ProofOnly_NullProofValidator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ReceiptValidator((InclusionProofValidator)null!));
        ex.ParamName.Should().Be("proofValidator");
    }

    #endregion

    #region Verify - Valid Signature Tests

    [Fact]
    public void Verify_ValidSignatureAndProof_ReturnsValid()
    {
        // Arrange
        var leafHash = ComputeHash("tx-leaf");
        var receipt = CreateValidReceipt(leafHash, leafHash);

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeTrue();
        result.SignatureValid.Should().BeTrue();
        result.InclusionProofValid.Should().BeTrue();
        result.MerkleRootConsistent.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Verify - Tampered Receipt Tests

    [Fact]
    public void Verify_TamperedSignature_ReturnsInvalidSignature()
    {
        // Arrange: Signature verification returns false (tampered data)
        var leafHash = ComputeHash("tx-leaf");
        var receipt = CreateValidReceipt(leafHash, leafHash);

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => false;
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SignatureValid.Should().BeFalse();
        result.InclusionProofValid.Should().BeTrue();
        result.MerkleRootConsistent.Should().BeTrue();
    }

    [Fact]
    public void Verify_SignatureVerificationThrows_ReturnsError()
    {
        // Arrange: Signature verification throws
        var leafHash = ComputeHash("tx-leaf");
        var receipt = CreateValidReceipt(leafHash, leafHash);

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => throw new CryptographicException("Bad key");
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SignatureValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Signature verification failed"));
    }

    [Fact]
    public void Verify_NoSignatures_ReturnsError()
    {
        // Arrange: Receipt with no signatures
        var leafHash = ComputeHash("tx-leaf");
        var receipt = CreateValidReceipt(leafHash, leafHash) with
        {
            Signatures = Array.Empty<ReceiptSignature>()
        };

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeFalse();
        result.SignatureValid.Should().BeFalse();
        result.Errors.Should().Contain("Receipt has no validator signatures");
    }

    #endregion

    #region Verify - Tampered Proof Tests

    [Fact]
    public void Verify_TamperedInclusionProof_ReturnsInvalidProof()
    {
        // Arrange: Valid signature but broken inclusion proof (wrong merkle root in proof)
        var leafHash = ComputeHash("tx-leaf");
        var wrongRoot = ComputeHash("wrong-root");

        var receipt = CreateValidReceipt(leafHash, leafHash) with
        {
            InclusionProof = new MerkleInclusionProof
            {
                TransactionHash = leafHash,
                DocketNumber = 1,
                MerkleRoot = wrongRoot, // Proof has wrong root
                ProofPath = new List<MerkleProofStep>(),
                LeafIndex = 0,
                TreeSize = 1
            }
        };

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InclusionProofValid.Should().BeFalse();
        result.MerkleRootConsistent.Should().BeFalse(); // Receipt root != proof root
    }

    [Fact]
    public void Verify_MerkleRootMismatchBetweenReceiptAndProof_ReturnsInconsistent()
    {
        // Arrange: Proof is internally valid but receipt's MerkleRoot differs from proof's
        var leafHash = ComputeHash("tx-leaf");
        var differentRoot = ComputeHash("different-root");

        var receipt = CreateValidReceipt(leafHash, leafHash) with
        {
            MerkleRoot = differentRoot // Receipt has different root than its embedded proof
        };

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var validator = new ReceiptValidator(_proofValidator, verifyFunc);

        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert
        result.IsValid.Should().BeFalse();
        result.MerkleRootConsistent.Should().BeFalse();
        result.Errors.Should().Contain("Receipt Merkle root does not match inclusion proof Merkle root");
    }

    #endregion

    #region Verify - Proof-Only Mode Tests

    [Fact]
    public void Verify_ProofOnlyValidator_WithSignatures_ReturnsNoVerificationFunc()
    {
        // Arrange: Validator without signature verification function
        var leafHash = ComputeHash("tx-leaf");
        var receipt = CreateValidReceipt(leafHash, leafHash);

        var validator = new ReceiptValidator(_proofValidator);
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Act
        var result = validator.Verify(receipt, publicKeyBase64);

        // Assert — proof-only mode: signature check is skipped, not failed
        result.IsValid.Should().BeTrue();
        result.SignatureValid.Should().BeFalse(); // Not verified, but not a failure
        result.SignatureCheckSkipped.Should().BeTrue();
        result.InclusionProofValid.Should().BeTrue();
        result.MerkleRootConsistent.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region BuildReceiptSigningData Tests

    [Fact]
    public void BuildReceiptSigningData_ProducesCanonicalFormat()
    {
        // Arrange
        var sealedAt = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero);
        var receipt = new TransactionReceipt
        {
            ReceiptId = "receipt-1",
            TransactionId = "tx-123",
            RegisterId = "reg-1",
            DocketNumber = 42,
            MerkleRoot = "abcd1234",
            InclusionProof = CreateSingleLeafProof("hash", "abcd1234"),
            Signatures = [],
            SealedAt = sealedAt
        };

        // Act
        var data = ReceiptValidator.BuildReceiptSigningData(receipt);
        var text = Encoding.UTF8.GetString(data);

        // Assert
        var expectedMs = sealedAt.ToUnixTimeMilliseconds();
        text.Should().Be($"tx-123|42|abcd1234|{expectedMs}");
    }

    #endregion

    #region Helpers

    private string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private TransactionReceipt CreateValidReceipt(string leafHash, string merkleRoot)
    {
        return new TransactionReceipt
        {
            ReceiptId = "receipt-1",
            TransactionId = "tx-1",
            RegisterId = "register-1",
            DocketNumber = 1,
            MerkleRoot = merkleRoot,
            InclusionProof = CreateSingleLeafProof(leafHash, merkleRoot),
            Signatures = new List<ReceiptSignature>
            {
                new()
                {
                    ValidatorAddress = "validator-wallet-1",
                    SignatureValue = new byte[] { 1, 2, 3, 4 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            SealedAt = DateTimeOffset.UtcNow
        };
    }

    private static MerkleInclusionProof CreateSingleLeafProof(string txHash, string merkleRoot)
    {
        return new MerkleInclusionProof
        {
            TransactionHash = txHash,
            DocketNumber = 1,
            MerkleRoot = merkleRoot,
            ProofPath = new List<MerkleProofStep>(),
            LeafIndex = 0,
            TreeSize = 1
        };
    }

    #endregion
}
