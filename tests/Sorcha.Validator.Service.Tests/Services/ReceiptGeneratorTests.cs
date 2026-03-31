// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ReceiptGenerator (T024).
/// Verifies receipt generation with Merkle inclusion proofs and signing.
/// </summary>
public class ReceiptGeneratorTests
{
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly Mock<ILogger<ReceiptGenerator>> _mockLogger;
    private readonly ReceiptGenerator _receiptGenerator;

    public ReceiptGeneratorTests()
    {
        _mockHashProvider = new Mock<IHashProvider>();

        // Use real SHA256 for deterministic hash computation
        _mockHashProvider
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<HashType>()))
            .Returns((byte[] data, HashType _) => SHA256.HashData(data));

        _merkleTree = new MerkleTree(_mockHashProvider.Object);
        _docketHasher = new DocketHasher(_mockHashProvider.Object);
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockLogger = new Mock<ILogger<ReceiptGenerator>>();

        _validatorConfig = new ValidatorConfiguration
        {
            ValidatorId = "validator-1",
            SystemWalletAddress = "system-wallet-address"
        };

        var options = Options.Create(_validatorConfig);

        // Default: signing succeeds with predictable results
        _mockWalletClient
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[] { 0xAA, 0xBB, 0xCC },
                PublicKey = new byte[32],
                SignedBy = "system-wallet-address",
                Algorithm = "ED25519"
            });

        _receiptGenerator = new ReceiptGenerator(
            _merkleTree,
            _docketHasher,
            _mockWalletClient.Object,
            options,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullMerkleTree_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ReceiptGenerator(
            null!, _docketHasher, _mockWalletClient.Object,
            Options.Create(_validatorConfig), _mockLogger.Object));
        ex.ParamName.Should().Be("merkleTree");
    }

    [Fact]
    public void Constructor_NullDocketHasher_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ReceiptGenerator(
            _merkleTree, null!, _mockWalletClient.Object,
            Options.Create(_validatorConfig), _mockLogger.Object));
        ex.ParamName.Should().Be("docketHasher");
    }

    [Fact]
    public void Constructor_NullWalletClient_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ReceiptGenerator(
            _merkleTree, _docketHasher, null!,
            Options.Create(_validatorConfig), _mockLogger.Object));
        ex.ParamName.Should().Be("walletClient");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ReceiptGenerator(
            _merkleTree, _docketHasher, _mockWalletClient.Object,
            null!, _mockLogger.Object));
        ex.ParamName.Should().Be("config");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ReceiptGenerator(
            _merkleTree, _docketHasher, _mockWalletClient.Object,
            Options.Create(_validatorConfig), null!));
        ex.ParamName.Should().Be("logger");
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - Single Transaction

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_SingleTransaction_ReturnsOneReceipt()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 1);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        receipts.Should().HaveCount(1);

        var receipt = receipts[0];
        receipt.TransactionId.Should().Be(docket.Transactions[0].TransactionId);
        receipt.RegisterId.Should().Be(docket.RegisterId);
        receipt.DocketNumber.Should().Be(docket.DocketNumber);
        receipt.MerkleRoot.Should().Be(docket.MerkleRoot);
        receipt.ReceiptId.Should().NotBeNullOrEmpty();
        receipt.Signatures.Should().HaveCount(1);
        receipt.Signatures[0].ValidatorAddress.Should().Be(_validatorConfig.SystemWalletAddress);
        receipt.Signatures[0].Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_SingleTransaction_HasValidInclusionProof()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 1);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        var proof = receipts[0].InclusionProof;
        proof.Should().NotBeNull();
        proof.DocketNumber.Should().Be(docket.DocketNumber);
        proof.TreeSize.Should().Be(1);
        proof.LeafIndex.Should().Be(0);
        proof.ProofPath.Should().BeEmpty(); // Single-leaf tree has empty proof path
        proof.MerkleRoot.Should().Be(proof.TransactionHash); // Single leaf IS the root
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - Five Transactions

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_FiveTransactions_ReturnsFiveReceipts()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 5);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        receipts.Should().HaveCount(5);

        for (int i = 0; i < 5; i++)
        {
            receipts[i].TransactionId.Should().Be(docket.Transactions[i].TransactionId);
            receipts[i].RegisterId.Should().Be(docket.RegisterId);
            receipts[i].DocketNumber.Should().Be(docket.DocketNumber);
            receipts[i].Signatures.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_FiveTransactions_EachHasValidProofPath()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 5);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: Each receipt's inclusion proof should have non-empty proof path
        // (since there are multiple leaves) and consistent tree size
        foreach (var receipt in receipts)
        {
            receipt.InclusionProof.TreeSize.Should().Be(5);
            receipt.InclusionProof.ProofPath.Should().NotBeEmpty();
            receipt.InclusionProof.MerkleRoot.Should().Be(docket.MerkleRoot);
        }
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_FiveTransactions_AllProofsVerifyAgainstRoot()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 5);
        var proofValidator = new Sorcha.Validator.Core.InclusionProofValidator(_mockHashProvider.Object);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: Each proof should verify successfully using the InclusionProofValidator
        foreach (var receipt in receipts)
        {
            var result = proofValidator.Verify(receipt.InclusionProof);
            result.IsValid.Should().BeTrue(
                $"Proof for transaction index {receipt.InclusionProof.LeafIndex} should verify against root");
        }
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - 100 Transactions

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_100Transactions_Returns100Receipts()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 100);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        receipts.Should().HaveCount(100);

        // All receipts should have unique receipt IDs
        var uniqueIds = receipts.Select(r => r.ReceiptId).Distinct();
        uniqueIds.Should().HaveCount(100);
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_100Transactions_AllProofsVerify()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 100);
        var proofValidator = new Sorcha.Validator.Core.InclusionProofValidator(_mockHashProvider.Object);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: All 100 proofs should verify
        foreach (var receipt in receipts)
        {
            var result = proofValidator.Verify(receipt.InclusionProof);
            result.IsValid.Should().BeTrue(
                $"Proof for leaf index {receipt.InclusionProof.LeafIndex} should be valid");
        }
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_100Transactions_ProofPathLengthIsLogN()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 100);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: Proof path length should be ceil(log2(100)) = 7
        foreach (var receipt in receipts)
        {
            receipt.InclusionProof.ProofPath.Count.Should().BeInRange(1, 7,
                "proof path for 100 leaves should be at most log2(100) ≈ 7 steps");
        }
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - Empty Docket

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_EmptyDocket_ReturnsEmptyArray()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 0);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        receipts.Should().BeEmpty();
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - Signing Behavior

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_CallsSignDataForEachTransaction()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 3);

        // Act
        await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: SignDataAsync should be called once per transaction
        _mockWalletClient.Verify(
            w => w.SignDataAsync(
                _validatorConfig.SystemWalletAddress,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_SigningFails_ReturnsReceiptWithoutSignature()
    {
        // Arrange
        _mockWalletClient
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Wallet service unavailable"));

        var docket = CreateDocket(transactionCount: 1);

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: Receipt still created but without signatures
        receipts.Should().HaveCount(1);
        receipts[0].Signatures.Should().BeEmpty();
        receipts[0].ReceiptId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_UsesConsensusAchievedAtForSealedAt()
    {
        // Arrange
        var consensusTime = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var docket = CreateDocket(transactionCount: 1);
        docket.ConsensusAchievedAt = consensusTime;

        // Act
        var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert
        receipts[0].SealedAt.Should().Be(consensusTime);
    }

    #endregion

    #region GenerateReceiptsForDocketAsync - Receipt ID Determinism

    [Fact]
    public async Task GenerateReceiptsForDocketAsync_ReceiptIdIsDeterministic()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 1);

        // Act
        var receipts1 = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);
        var receipts2 = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket);

        // Assert: Same input should produce same receipt ID
        receipts1[0].ReceiptId.Should().Be(receipts2[0].ReceiptId);
    }

    #endregion

    #region Helpers

    private Sorcha.Validator.Service.Models.Docket CreateDocket(int transactionCount)
    {
        var transactions = Enumerable.Range(0, transactionCount)
            .Select(i => new Transaction
            {
                TransactionId = $"tx-{i:D4}",
                RegisterId = "register-1",
                Payload = JsonSerializer.SerializeToElement(new { data = $"payload-{i}" }),
                PayloadHash = $"payload-hash-{i:D4}",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-transactionCount + i),
                Signatures = new List<Signature>
                {
                    new()
                    {
                        PublicKey = new byte[32],
                        SignatureValue = new byte[] { 1, 2, 3 },
                        Algorithm = "ED25519",
                        SignedAt = DateTimeOffset.UtcNow
                    }
                }
            })
            .ToList();

        // Compute Merkle root from transaction hashes (matching what ReceiptGenerator does)
        var txHashes = transactions
            .Select(tx => _docketHasher.ComputeTransactionHash(tx.TransactionId, tx.PayloadHash, tx.CreatedAt))
            .ToList();

        var merkleRoot = transactionCount > 0
            ? _merkleTree.ComputeMerkleRoot(txHashes)
            : "empty-root";

        return new Sorcha.Validator.Service.Models.Docket
        {
            DocketId = "docket-1",
            RegisterId = "register-1",
            DocketNumber = 42,
            DocketHash = "docket-hash-1",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ConsensusAchievedAt = DateTimeOffset.UtcNow,
            Transactions = transactions,
            MerkleRoot = merkleRoot,
            ProposerValidatorId = "validator-1",
            ProposerSignature = new Signature
            {
                PublicKey = new byte[32],
                SignatureValue = new byte[] { 1, 2, 3 },
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            }
        };
    }

    #endregion
}
