// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Validator.Core;
using Xunit;

namespace Sorcha.Validator.Core.Tests;

/// <summary>
/// Unit tests for BundleVerifier (T043).
/// Validates offline verification bundle checking — all four verification components.
/// </summary>
public class BundleVerifierTests
{
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly InclusionProofValidator _inclusionProofValidator;

    public BundleVerifierTests()
    {
        _mockHashProvider = new Mock<IHashProvider>();
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) => SHA256.HashData(data));

        _inclusionProofValidator = new InclusionProofValidator(_mockHashProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullReceiptValidator_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BundleVerifier(null!, _inclusionProofValidator));
        ex.ParamName.Should().Be("receiptValidator");
    }

    [Fact]
    public void Constructor_NullInclusionProofValidator_ThrowsArgumentNullException()
    {
        // Arrange
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BundleVerifier(receiptValidator, null!));
        ex.ParamName.Should().Be("inclusionProofValidator");
    }

    #endregion

    #region VerifyBundle - All Valid Tests

    [Fact]
    public void VerifyBundle_AllValid_ReturnsValidResult()
    {
        // Arrange
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        var receipt = CreateValidReceipt(leafHash, validatorAddress);
        var bundle = CreateValidBundle(receipt, validatorAddress);
        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Checks.CredentialSignatureValid.Should().BeTrue();
        result.Checks.InclusionProofValid.Should().BeTrue();
        result.Checks.ReceiptSignatureValid.Should().BeTrue();
        result.Checks.RevocationStatusCurrent.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region VerifyBundle - Invalid Inclusion Proof Tests

    [Fact]
    public void VerifyBundle_InvalidInclusionProof_ReturnsInvalid()
    {
        // Arrange
        var leafHash = ComputeHash("tx-leaf");
        var wrongRoot = ComputeHash("wrong-root");

        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        // Create receipt with broken inclusion proof
        var receipt = new TransactionReceipt
        {
            ReceiptId = "receipt-1",
            TransactionId = "tx-1",
            RegisterId = "register-1",
            DocketNumber = 1,
            MerkleRoot = wrongRoot,
            InclusionProof = new MerkleInclusionProof
            {
                TransactionHash = leafHash,
                DocketNumber = 1,
                MerkleRoot = wrongRoot, // Root doesn't match what leaf computes to
                ProofPath = new List<MerkleProofStep>
                {
                    new() { Hash = ComputeHash("sibling"), Position = ProofPosition.Right }
                },
                LeafIndex = 0,
                TreeSize = 2
            },
            Signatures = new List<ReceiptSignature>
            {
                new()
                {
                    ValidatorAddress = validatorAddress,
                    SignatureValue = new byte[] { 1, 2, 3 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            SealedAt = DateTimeOffset.UtcNow
        };

        var bundle = CreateValidBundle(receipt, validatorAddress);
        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Checks.InclusionProofValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("does not match expected") || e.Contains("Inclusion proof"));
    }

    #endregion

    #region VerifyBundle - Invalid Receipt Signature Tests

    [Fact]
    public void VerifyBundle_InvalidReceiptSignature_ReturnsInvalid()
    {
        // Arrange: Signature verification fails
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => false;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        var receipt = CreateValidReceipt(leafHash, validatorAddress);
        var bundle = CreateValidBundle(receipt, validatorAddress);
        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Checks.ReceiptSignatureValid.Should().BeFalse();
    }

    [Fact]
    public void VerifyBundle_NoMatchingValidatorKey_ReturnsError()
    {
        // Arrange: Validator key doesn't match the signer address
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var receipt = CreateValidReceipt(leafHash, "validator-wallet-1");
        var bundle = CreateValidBundle(receipt, "validator-wallet-1");

        // Keys for a different validator
        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new()
            {
                Address = "different-validator",
                PublicKey = Convert.ToBase64String(new byte[32]),
                Algorithm = "ED25519"
            }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Checks.ReceiptSignatureValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("No matching public key"));
    }

    [Fact]
    public void VerifyBundle_EmptyValidatorKeys_ReturnsError()
    {
        // Arrange
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var receipt = CreateValidReceipt(leafHash, "validator-1");
        var bundle = CreateValidBundle(receipt, "validator-1");

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, Array.Empty<ValidatorKeyInfo>());

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Contains("No validator public keys or receipt signatures available"));
    }

    #endregion

    #region VerifyBundle - Revocation Status Tests

    [Fact]
    public void VerifyBundle_RevokedTransaction_ReturnsWarning()
    {
        // Arrange: Transaction was revoked at export time
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        var receipt = CreateValidReceipt(leafHash, validatorAddress);
        var bundle = CreateValidBundle(receipt, validatorAddress) with
        {
            RevocationStatus = new TransactionStatusResponse
            {
                TransactionId = "tx-1",
                Status = TransactionLifecycleStatus.Revoked,
                RevocationTxId = "revocation-tx-1",
                RevokedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Reason = RevocationReason.Erroneous
            }
        };

        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        // IsValid depends on other checks being valid (credential + proof + sig)
        result.Checks.RevocationStatusCurrent.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("Revoked"));
    }

    [Fact]
    public void VerifyBundle_StaleBundle_AddsAgeWarning()
    {
        // Arrange: Bundle exported more than 24 hours ago
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        var receipt = CreateValidReceipt(leafHash, validatorAddress);
        var bundle = CreateValidBundle(receipt, validatorAddress) with
        {
            ExportedAt = DateTimeOffset.UtcNow.AddDays(-3) // 3 days old
        };

        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("exported") && w.Contains("days ago"));
    }

    #endregion

    #region VerifyBundle - Missing Credential Tests

    [Fact]
    public void VerifyBundle_NullCredential_ReturnsError()
    {
        // Arrange
        var leafHash = ComputeHash("tx-leaf");
        SignatureVerifyFunc verifyFunc = (_, _, _, _) => true;
        var receiptValidator = new ReceiptValidator(_inclusionProofValidator, verifyFunc);
        var bundleVerifier = new BundleVerifier(receiptValidator, _inclusionProofValidator);

        var validatorAddress = "validator-wallet-1";
        var publicKeyBase64 = Convert.ToBase64String(new byte[32]);

        var receipt = CreateValidReceipt(leafHash, validatorAddress);

        // Create bundle with null/undefined credential
        var bundle = new VerificationBundle
        {
            TransactionId = "tx-1",
            RegisterId = "register-1",
            Credential = default, // JsonElement undefined
            Receipt = receipt,
            RevocationStatus = new TransactionStatusResponse
            {
                TransactionId = "tx-1",
                Status = TransactionLifecycleStatus.Active
            },
            ExportedAt = DateTimeOffset.UtcNow,
            ValidatorPublicKeys = Array.Empty<ValidatorKeyInfo>()
        };

        var validatorKeys = new List<ValidatorKeyInfo>
        {
            new() { Address = validatorAddress, PublicKey = publicKeyBase64, Algorithm = "ED25519" }
        };

        // Act
        var result = bundleVerifier.VerifyBundle(bundle, validatorKeys);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Checks.CredentialSignatureValid.Should().BeFalse();
        result.Errors.Should().Contain("Credential data is missing or null");
    }

    #endregion

    #region Helpers

    private string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private TransactionReceipt CreateValidReceipt(string leafHash, string validatorAddress)
    {
        return new TransactionReceipt
        {
            ReceiptId = "receipt-1",
            TransactionId = "tx-1",
            RegisterId = "register-1",
            DocketNumber = 1,
            MerkleRoot = leafHash, // Single-leaf tree: root == leaf
            InclusionProof = new MerkleInclusionProof
            {
                TransactionHash = leafHash,
                DocketNumber = 1,
                MerkleRoot = leafHash,
                ProofPath = new List<MerkleProofStep>(),
                LeafIndex = 0,
                TreeSize = 1
            },
            Signatures = new List<ReceiptSignature>
            {
                new()
                {
                    ValidatorAddress = validatorAddress,
                    SignatureValue = new byte[] { 1, 2, 3, 4 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            SealedAt = DateTimeOffset.UtcNow
        };
    }

    private VerificationBundle CreateValidBundle(TransactionReceipt receipt, string validatorAddress)
    {
        var credentialJson = JsonSerializer.SerializeToElement(new { type = "TestCredential", data = "test" });

        return new VerificationBundle
        {
            TransactionId = receipt.TransactionId,
            RegisterId = receipt.RegisterId,
            Credential = credentialJson,
            Receipt = receipt,
            RevocationStatus = new TransactionStatusResponse
            {
                TransactionId = receipt.TransactionId,
                Status = TransactionLifecycleStatus.Active
            },
            ExportedAt = DateTimeOffset.UtcNow,
            ValidatorPublicKeys = new List<ValidatorKeyInfo>
            {
                new()
                {
                    Address = validatorAddress,
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "ED25519"
                }
            }
        };
    }

    #endregion
}
