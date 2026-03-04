// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Validator.Service.Tests.Services;

public class ValidationEngineTests
{
    private readonly Mock<IBlueprintCache> _blueprintCacheMock;
    private readonly Mock<IHashProvider> _hashProviderMock;
    private readonly Mock<ICryptoModule> _cryptoModuleMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IRightsEnforcementService> _rightsEnforcementMock;
    private readonly Mock<IWalletUtilities> _walletUtilitiesMock;
    private readonly Mock<ILogger<ValidationEngine>> _loggerMock;
    private readonly ValidationEngineConfiguration _config;
    private readonly ValidationEngine _engine;

    public ValidationEngineTests()
    {
        _blueprintCacheMock = new Mock<IBlueprintCache>();
        _hashProviderMock = new Mock<IHashProvider>();
        _cryptoModuleMock = new Mock<ICryptoModule>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _rightsEnforcementMock = new Mock<IRightsEnforcementService>();
        _walletUtilitiesMock = new Mock<IWalletUtilities>();
        _loggerMock = new Mock<ILogger<ValidationEngine>>();

        // Default: no existing successors (no fork)
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1 });

        // Default: governance validation passes (non-governance transactions pass through)
        _rightsEnforcementMock.Setup(r => r.ValidateGovernanceRightsAsync(
                It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Transaction tx, CancellationToken _) =>
                ValidationEngineResult.Success(tx.TransactionId, tx.RegisterId, TimeSpan.Zero));

        _config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = true,
            EnableSignatureVerification = true,
            EnableChainValidation = true,
            EnableBlueprintConformance = false,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };

        _engine = new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            null!,
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_WithNullBlueprintCache_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            null!,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("blueprintCache");
    }

    [Fact]
    public void Constructor_WithNullHashProvider_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            null!,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("hashProvider");
    }

    [Fact]
    public void Constructor_WithNullCryptoModule_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            null!,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("cryptoModule");
    }

    [Fact]
    public void Constructor_WithNullWalletUtilities_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            null!,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("walletUtilities");
    }

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            null!,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerClient");
    }

    [Fact]
    public void Constructor_WithNullRightsEnforcement_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            null!,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("rightsEnforcementService");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ValidationEngine(
            Options.Create(_config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void ValidateStructure_WithValidTransaction_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction();

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateStructure_WithMissingTransactionId_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(transactionId: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_001");
    }

    [Fact]
    public void ValidateStructure_WithMissingRegisterId_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(registerId: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_002");
    }

    [Fact]
    public void ValidateStructure_WithMissingBlueprintId_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(blueprintId: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_003");
    }

    [Fact]
    public void ValidateStructure_WithMissingSignatures_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(signatures: []);

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_007");
    }

    [Fact]
    public void ValidateStructure_WithMissingActionId_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(actionId: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_004");
    }

    [Fact]
    public void ValidateStructure_WithMissingPayloadHash_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(payloadHash: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_006");
    }

    [Fact]
    public void ValidateStructure_WithSignatureMissingPublicKey_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(signatures:
        [
            new Signature
            {
                PublicKey = [],
                SignatureValue = new byte[64],
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            }
        ]);

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_008");
    }

    [Fact]
    public void ValidateStructure_WithSignatureMissingSignatureValue_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(signatures:
        [
            new Signature
            {
                PublicKey = new byte[32],
                SignatureValue = [],
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            }
        ]);

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_009");
    }

    [Fact]
    public void ValidateStructure_WithSignatureMissingAlgorithm_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(signatures:
        [
            new Signature
            {
                PublicKey = new byte[32],
                SignatureValue = new byte[64],
                Algorithm = "",
                SignedAt = DateTimeOffset.UtcNow
            }
        ]);

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_010");
    }

    [Fact]
    public void ValidateStructure_WithNullTransaction_ThrowsArgumentNullException()
    {
        var act = () => _engine.ValidateStructure(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithValidTransaction_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction();
        SetupSuccessfulValidation(tx);

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        result.TransactionId.Should().Be(tx.TransactionId);
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithInvalidPayloadHash_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(payloadHash: "invalid_hash");

        // Setup hash computation to return different hash
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns([0x01, 0x02, 0x03]);

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_HASH_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_WhenBlueprintNotFound_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction();
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BlueprintModel?)null);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_WhenActionNotFound_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(actionId: "999");
        var blueprint = CreateTestBlueprint(tx.BlueprintId);

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_003");
    }

    [Fact]
    public async Task ValidateBatchAsync_ValidatesAllTransactions()
    {
        // Arrange
        var transactions = new List<Transaction>
        {
            CreateValidTransaction("tx-1"),
            CreateValidTransaction("tx-2"),
            CreateValidTransaction("tx-3")
        };

        foreach (var tx in transactions)
        {
            SetupSuccessfulValidation(tx);
        }

        // Act
        var results = await _engine.ValidateBatchAsync(transactions);

        // Assert
        results.Should().HaveCount(3);
        results.Should().OnlyContain(r => r.IsValid);
    }

    [Fact]
    public async Task GetStats_ReturnsCorrectStatistics()
    {
        // Arrange - Validate some transactions using the async method which records stats
        var validTx = CreateValidTransaction();
        SetupSuccessfulValidation(validTx);
        await _engine.ValidateTransactionAsync(validTx);

        var invalidTx = CreateValidTransaction(transactionId: "");
        await _engine.ValidateTransactionAsync(invalidTx);

        // Act
        var stats = _engine.GetStats();

        // Assert
        stats.TotalValidated.Should().BeGreaterThan(0);
        stats.TotalSuccessful.Should().BeGreaterThan(0);
        stats.TotalFailed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithFutureTimestamp_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(createdAt: DateTimeOffset.UtcNow.AddHours(1));
        SetupSuccessfulValidation(tx);

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_TIME_001");
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithExpiredTransaction_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        SetupSuccessfulValidation(tx);

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_TIME_003");
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithOldTransaction_ReturnsFailed()
    {
        // Arrange - transaction older than MaxTransactionAge
        var tx = CreateValidTransaction(createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        SetupSuccessfulValidation(tx);

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_TIME_002");
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        var act = async () => await _engine.ValidateTransactionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public async Task VerifySignaturesAsync_WithValidSignature_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction();
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);
        _cryptoModuleMock.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        // Act
        var result = await _engine.VerifySignaturesAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifySignaturesAsync_WithInvalidSignature_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction();
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);
        _cryptoModuleMock.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.InvalidSignature);

        // Act
        var result = await _engine.VerifySignaturesAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SIG_002");
    }

    [Fact]
    public async Task VerifySignaturesAsync_WithUnsupportedAlgorithm_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(signatures:
        [
            new Signature
            {
                PublicKey = new byte[32],
                SignatureValue = new byte[64],
                Algorithm = "UNKNOWN_ALGO",
                SignedAt = DateTimeOffset.UtcNow
            }
        ]);
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(new byte[32]);

        // Act
        var result = await _engine.VerifySignaturesAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SIG_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_WithInvalidActionIdFormat_ReturnsFailed()
    {
        // Arrange
        var tx = CreateValidTransaction(actionId: "not-a-number");
        var blueprint = CreateTestBlueprint(tx.BlueprintId);

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_002");
    }

    [Fact]
    public async Task ValidateBatchAsync_WithEmptyList_ReturnsEmptyResults()
    {
        // Arrange
        var transactions = new List<Transaction>();

        // Act
        var results = await _engine.ValidateBatchAsync(transactions);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateBatchAsync_WithNullTransactions_ThrowsArgumentNullException()
    {
        var act = async () => await _engine.ValidateBatchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transactions");
    }

    [Fact]
    public async Task ValidateBatchAsync_WithMixedResults_ReturnsAllResults()
    {
        // Arrange
        var validTx = CreateValidTransaction("valid-tx");
        var invalidTx = CreateValidTransaction("invalid-tx", payloadHash: "bad_hash");
        var transactions = new List<Transaction> { validTx, invalidTx };

        SetupSuccessfulValidation(validTx);
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns((byte[] data, HashType _) =>
            {
                // Return correct hash only for valid tx
                var payloadHash = Convert.FromHexString(validTx.PayloadHash);
                return payloadHash;
            });

        // Act
        var results = await _engine.ValidateBatchAsync(transactions);

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.TransactionId == "valid-tx" && r.IsValid);
        results.Should().Contain(r => r.TransactionId == "invalid-tx" && !r.IsValid);
    }

    [Fact]
    public async Task ValidateTransactionAsync_WithStructureErrors_DoesNotContinueValidation()
    {
        // Arrange - Transaction with empty ID should fail structure validation
        var tx = CreateValidTransaction(transactionId: "");

        // Act
        var result = await _engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Category == ValidationErrorCategory.Structure);
        // Blueprint cache should NOT be called because structure validation failed
        _blueprintCacheMock.Verify(
            c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateChainAsync_NoPreviousTransaction_ReturnsSuccess()
    {
        // Arrange - genesis transaction (no previous), empty register
        var tx = CreateValidTransaction();
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateChainAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        var act = async () => await _engine.ValidateChainAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _engine.GetStats();

        // Assert
        stats.TotalValidated.Should().Be(0);
        stats.TotalSuccessful.Should().Be(0);
        stats.TotalFailed.Should().Be(0);
        stats.InProgress.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_SuccessRate_CalculatesCorrectly()
    {
        // Arrange - Need to process some transactions first
        var validTx = CreateValidTransaction();
        SetupSuccessfulValidation(validTx);
        await _engine.ValidateTransactionAsync(validTx);

        var invalidTx = CreateValidTransaction(transactionId: "");
        await _engine.ValidateTransactionAsync(invalidTx);

        // Act
        var stats = _engine.GetStats();

        // Assert
        stats.SuccessRate.Should().BeApproximately(0.5, 0.01);
    }

    #region Schema Validation Tests (US1)

    [Fact]
    public async Task ValidateSchemaAsync_ValidPayload_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction(payloadJson: """{"name":"Alice","amount":100}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_MissingRequiredField_ReturnsSchemaError()
    {
        // Arrange - payload missing "amount"
        var tx = CreateValidTransaction(payloadJson: """{"name":"Alice"}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_004");
        result.Errors.Should().Contain(e => e.Category == ValidationErrorCategory.Schema);
    }

    [Fact]
    public async Task ValidateSchemaAsync_WrongType_ReturnsSchemaError()
    {
        // Arrange - amount is string instead of number
        var tx = CreateValidTransaction(payloadJson: """{"name":"Alice","amount":"not-a-number"}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_004");
    }

    [Fact]
    public async Task ValidateSchemaAsync_NoSchemas_ReturnsSuccess()
    {
        // Arrange - action has no DataSchemas (null)
        var tx = CreateValidTransaction();
        var blueprint = CreateTestBlueprint(tx.BlueprintId); // no schemas
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_EmptySchemas_ReturnsSuccess()
    {
        // Arrange - action has empty DataSchemas list
        var tx = CreateValidTransaction();
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId, schemaJson: null);
        // Override the action to have empty DataSchemas
        blueprint.Actions.First().DataSchemas = [];
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_DisabledByConfig_ReturnsSuccess()
    {
        // Arrange - disable schema validation
        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = false,
            EnableSignatureVerification = true,
            EnableChainValidation = true,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };
        var engine = new ValidationEngine(
            Options.Create(config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        var tx = CreateValidTransaction();

        // Act
        var result = await engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        _blueprintCacheMock.Verify(
            c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateSchemaAsync_MalformedSchema_ReturnsBlueprintError()
    {
        // Arrange - action has invalid JSON schema
        var tx = CreateValidTransaction(payloadJson: """{"name":"Alice"}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId,
            schemaJson: """{"type": "invalid-type-value", "$schema": "not-a-schema"}""");
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert - malformed schema should fail but may produce VAL_SCHEMA_004 or VAL_SCHEMA_005
        // depending on whether JsonSchema.Net can parse but evaluates badly, or throws
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateSchemaAsync_MultipleSchemas_AllMustPass()
    {
        // Arrange - two schemas: first requires "name", second requires "email"
        var tx = CreateValidTransaction(payloadJson: """{"name":"Alice"}"""); // passes first, fails second

        var schema1 = """{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}""";
        var schema2 = """{"type":"object","required":["email"],"properties":{"email":{"type":"string"}}}""";

        var blueprint = new BlueprintModel
        {
            Id = tx.BlueprintId,
            Title = "Test Blueprint",
            Participants = [new Sorcha.Blueprint.Models.Participant { Id = "p-1", Name = "P1" }],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Test Action",
                    Sender = "p-1",
                    DataSchemas =
                    [
                        JsonDocument.Parse(schema1),
                        JsonDocument.Parse(schema2)
                    ]
                }
            ]
        };

        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_004");
    }

    [Fact]
    public async Task ValidateSchemaAsync_MultipleViolations_AllReported()
    {
        // Arrange - payload missing "name" (required) AND "amount" is wrong type
        var tx = CreateValidTransaction(payloadJson: """{"amount":"not-a-number"}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Where(e => e.Code == "VAL_SCHEMA_004").Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ValidateSchemaAsync_NestedObjectViolation_IncludesJsonPath()
    {
        // Arrange - schema requires nested object
        var schemaJson = """
        {
            "type": "object",
            "required": ["address"],
            "properties": {
                "address": {
                    "type": "object",
                    "required": ["zipCode"],
                    "properties": {
                        "zipCode": { "type": "string" }
                    }
                }
            }
        }
        """;
        var tx = CreateValidTransaction(payloadJson: """{"address":{"zipCode":12345}}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId, schemaJson);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == "VAL_SCHEMA_004" &&
            e.Field != null && e.Field.Contains("zipCode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateSchemaAsync_EnumViolation_ReportsAllowedValues()
    {
        // Arrange - schema with enum constraint
        var schemaJson = """
        {
            "type": "object",
            "required": ["status"],
            "properties": {
                "status": { "type": "string", "enum": ["active", "inactive", "pending"] }
            }
        }
        """;
        var tx = CreateValidTransaction(payloadJson: """{"status":"deleted"}""");
        var blueprint = CreateTestBlueprintWithSchema(tx.BlueprintId, schemaJson);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_SCHEMA_004" && e.Field != null && e.Field.Contains("status"));
    }

    #endregion

    #region Chain Validation Tests (US2)

    [Fact]
    public async Task ValidateChainAsync_EmptyPreviousTransaction_ReturnsSuccess()
    {
        // Arrange - empty string treated as null (FR-009)
        var tx = CreateValidTransaction(previousTransactionId: "");
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateChainAsync_ValidPreviousTransaction_ReturnsSuccess()
    {
        // Arrange
        var previousTxId = "prev-tx-001";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { RegisterId = tx.RegisterId, TxId = previousTxId });
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateChainAsync_PreviousTransactionNotFound_ReturnsChainError()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: "nonexistent-tx");

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "nonexistent-tx", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.TransactionModel?)null);
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_001");
    }

    [Fact]
    public async Task ValidateChainAsync_PreviousTransactionWrongRegister_ReturnsChainError()
    {
        // Arrange
        var previousTxId = "prev-tx-wrong-register";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { RegisterId = "different-register", TxId = previousTxId });
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_002");
    }

    [Fact]
    public async Task ValidateChainAsync_DisabledByConfig_ReturnsSuccess()
    {
        // Arrange
        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = true,
            EnableSignatureVerification = true,
            EnableChainValidation = false,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };
        var engine = new ValidationEngine(
            Options.Create(config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        var tx = CreateValidTransaction(previousTransactionId: "some-tx");

        // Act
        var result = await engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        _registerClientMock.Verify(
            r => r.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateChainAsync_RegisterServiceUnavailable_ReturnsTransientError()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: "some-tx");

        _registerClientMock.Setup(r => r.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_TRANSIENT" && !e.IsFatal);
    }

    [Fact]
    public async Task ValidateChainAsync_DocketChainIntact_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction();

        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "docket-2",
                RegisterId = tx.RegisterId,
                DocketNumber = 2,
                PreviousHash = "hash-of-docket-1",
                DocketHash = "hash-of-docket-2",
                CreatedAt = DateTimeOffset.UtcNow,
                Transactions = [],
                ProposerValidatorId = "val-1",
                MerkleRoot = "merkle-2"
            });
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "docket-1",
                RegisterId = tx.RegisterId,
                DocketNumber = 1,
                PreviousHash = null,
                DocketHash = "hash-of-docket-1",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Transactions = [],
                ProposerValidatorId = "val-1",
                MerkleRoot = "merkle-1"
            });

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateChainAsync_DocketHashMismatch_ReturnsChainError()
    {
        // Arrange
        var tx = CreateValidTransaction();

        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "docket-2",
                RegisterId = tx.RegisterId,
                DocketNumber = 2,
                PreviousHash = "WRONG-HASH",
                DocketHash = "hash-of-docket-2",
                CreatedAt = DateTimeOffset.UtcNow,
                Transactions = [],
                ProposerValidatorId = "val-1",
                MerkleRoot = "merkle-2"
            });
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "docket-1",
                RegisterId = tx.RegisterId,
                DocketNumber = 1,
                PreviousHash = null,
                DocketHash = "hash-of-docket-1",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Transactions = [],
                ProposerValidatorId = "val-1",
                MerkleRoot = "merkle-1"
            });

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_004");
    }

    [Fact]
    public async Task ValidateChainAsync_DocketGap_ReturnsChainError()
    {
        // Arrange
        var tx = CreateValidTransaction();

        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocketModel
            {
                DocketId = "docket-3",
                RegisterId = tx.RegisterId,
                DocketNumber = 3,
                PreviousHash = "some-hash",
                DocketHash = "hash-of-docket-3",
                CreatedAt = DateTimeOffset.UtcNow,
                Transactions = [],
                ProposerValidatorId = "val-1",
                MerkleRoot = "merkle-3"
            });
        _registerClientMock.Setup(r => r.ReadDocketAsync(tx.RegisterId, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocketModel?)null); // Gap!

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_003");
    }

    [Fact]
    public async Task ValidateChainAsync_GenesisRegister_ReturnsSuccess()
    {
        // Arrange - empty register (height 0)
        var tx = CreateValidTransaction();
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateChainAsync_NoFork_ZeroExistingSuccessors_ReturnsSuccess()
    {
        // Arrange — previous TX is an Action (non-Control), no existing successors
        var previousTxId = "prev-tx-no-fork";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                RegisterId = tx.RegisterId,
                TxId = previousTxId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { TransactionType = Sorcha.Register.Models.Enums.TransactionType.Action }
            });
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                tx.RegisterId, previousTxId, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1, Total = 0 });
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Code == "VAL_CHAIN_FORK");
    }

    [Fact]
    public async Task ValidateChainAsync_ForkDetected_ExistingSuccessors_ReturnsChainForkError()
    {
        // Arrange — previous TX is an Action (non-Control), another TX already claims the same predecessor
        var previousTxId = "prev-tx-forked";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                RegisterId = tx.RegisterId,
                TxId = previousTxId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { TransactionType = Sorcha.Register.Models.Enums.TransactionType.Action }
            });
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                tx.RegisterId, previousTxId, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1, Total = 1, Transactions = [new Sorcha.Register.Models.TransactionModel { TxId = "existing-fork-tx" }] });
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_FORK");
    }

    [Fact]
    public async Task ValidateChainAsync_ForkDetection_SkippedWhenPreviousTxIsControl()
    {
        // Arrange — previous TX is a Control transaction (e.g., blueprint-publish).
        // Multiple instances forking from the same Control TX is expected behaviour.
        var previousTxId = "prev-tx-control";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                RegisterId = tx.RegisterId,
                TxId = previousTxId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control }
            });
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Code == "VAL_CHAIN_FORK");
        _registerClientMock.Verify(
            r => r.GetTransactionsByPrevTxIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateChainAsync_ForkDetection_SkippedWhenPreviousTransactionIdIsNull()
    {
        // Arrange — no PreviousTransactionId means no fork detection
        var tx = CreateValidTransaction(previousTransactionId: null);
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        _registerClientMock.Verify(
            r => r.GetTransactionsByPrevTxIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateChainAsync_ForkDetection_SkippedWhenChainValidationDisabled()
    {
        // Arrange
        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = true,
            EnableSignatureVerification = true,
            EnableChainValidation = false,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };
        var engine = new ValidationEngine(
            Options.Create(config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        var tx = CreateValidTransaction(previousTransactionId: "some-tx");

        // Act
        var result = await engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        _registerClientMock.Verify(
            r => r.GetTransactionsByPrevTxIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateChainAsync_ForkDetection_TransientErrorOnServiceUnavailable()
    {
        // Arrange — GetTransactionAsync succeeds but fork detection throws
        var previousTxId = "prev-tx-transient";
        var tx = CreateValidTransaction(previousTransactionId: previousTxId);

        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, previousTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel { RegisterId = tx.RegisterId, TxId = previousTxId });
        _registerClientMock.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                tx.RegisterId, previousTxId, 1, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        // Act
        var result = await _engine.ValidateChainAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_CHAIN_TRANSIENT" && !e.IsFatal);
    }

    #endregion

    #region Blueprint Conformance Tests

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_GenesisTransaction_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction(blueprintId: "genesis");

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ControlTransaction_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction();
        tx.Metadata["Type"] = "Control";

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_StartingAction_NoPreTxId_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: true);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_NonStartingAction_NoPreTxId_ReturnsBP001Error()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: false);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_001");
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ValidSender_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: true, walletAddress: "wallet-abc");
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("wallet-abc");

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_WrongSender_ReturnsBP002Error()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: true, walletAddress: "expected-wallet");
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("wrong-wallet");

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_002");
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ParticipantNoWallet_SkipsCheck()
    {
        // Arrange
        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: true, walletAddress: null);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _walletUtilitiesMock.Setup(w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()))
            .Returns("any-wallet");

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ValidRouteSequence_ReturnsSuccess()
    {
        // Arrange — action 1 routes to action 2
        var tx = CreateValidTransaction(actionId: "2", previousTransactionId: "prev-tx-1");
        var blueprint = CreateTestBlueprintWithRoutes(tx.BlueprintId, fromActionId: 1, toActionIds: [2, 3]);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "prev-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "prev-tx-1",
                RegisterId = tx.RegisterId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { ActionId = 1 }
            });

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_InvalidRouteSequence_ReturnsBP003Error()
    {
        // Arrange — action 1 only routes to action 3, but current is action 2
        var tx = CreateValidTransaction(actionId: "2", previousTransactionId: "prev-tx-1");
        var blueprint = CreateTestBlueprintWithRoutes(tx.BlueprintId, fromActionId: 1, toActionIds: [3]);
        // Add action 2 to the blueprint so the action lookup succeeds (the test is about route validation, not missing actions)
        blueprint.Actions = blueprint.Actions.Append(new ActionModel { Id = 2, Title = "Action 2", Sender = "participant-1" }).ToList();
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "prev-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "prev-tx-1",
                RegisterId = tx.RegisterId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { ActionId = 1 }
            });

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_003");
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_RejectionRoute_ReturnsSuccess()
    {
        // Arrange — action 1 routes to action 3 (normal), rejection targets action 2
        var tx = CreateValidTransaction(actionId: "2", previousTransactionId: "prev-tx-1");
        var blueprint = CreateTestBlueprintWithRoutes(tx.BlueprintId, fromActionId: 1, toActionIds: [3], rejectionTargetActionId: 2);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "prev-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "prev-tx-1",
                RegisterId = tx.RegisterId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { ActionId = 1 }
            });

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_NoRoutesOnPreviousAction_SkipsSequenceCheck()
    {
        // Arrange — previous action has no routes defined
        var tx = CreateValidTransaction(actionId: "2", previousTransactionId: "prev-tx-1");
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: false);
        // Add action 2 to blueprint
        blueprint.Actions = blueprint.Actions.Append(new ActionModel { Id = 2, Title = "Action 2", Sender = "participant-1" }).ToList();
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "prev-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "prev-tx-1",
                RegisterId = tx.RegisterId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { ActionId = 1 }
            });

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert — no route check, should pass
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_PreviousTxMissingActionId_SkipsSequenceCheck()
    {
        // Arrange — previous transaction has no ActionId in metadata
        var tx = CreateValidTransaction(actionId: "2", previousTransactionId: "prev-tx-1");
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: false);
        blueprint.Actions = blueprint.Actions.Append(new ActionModel { Id = 2, Title = "Action 2", Sender = "participant-1" }).ToList();
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _registerClientMock.Setup(r => r.GetTransactionAsync(tx.RegisterId, "prev-tx-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.TransactionModel
            {
                TxId = "prev-tx-1",
                RegisterId = tx.RegisterId,
                MetaData = new Sorcha.Register.Models.TransactionMetaData { ActionId = null }
            });

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_DisabledByConfig_ReturnsSuccess()
    {
        // Arrange — disable blueprint conformance
        var config = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = true,
            EnableSignatureVerification = true,
            EnableChainValidation = true,
            EnableParallelValidation = false,
            EnableBlueprintConformance = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };
        var engine = new ValidationEngine(
            Options.Create(config),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        var tx = CreateValidTransaction(previousTransactionId: null);
        SetupSuccessfulValidation(tx);

        // Act — run full pipeline, conformance should be skipped
        var result = await engine.ValidateTransactionAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        // WalletUtilities should never be called
        _walletUtilitiesMock.Verify(
            w => w.PublicKeyToWallet(It.IsAny<byte[]>(), It.IsAny<byte>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateTransactionAsync_FullPipeline_IncludesBlueprintConformance()
    {
        // Arrange — non-starting action without PrevTxId should produce BP_001 error in full pipeline
        // Use a dedicated engine with EnableBlueprintConformance = true
        var conformanceConfig = new ValidationEngineConfiguration
        {
            EnableSchemaValidation = true,
            EnableSignatureVerification = true,
            EnableChainValidation = true,
            EnableBlueprintConformance = true,
            EnableParallelValidation = false,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxTransactionAge = TimeSpan.FromHours(1)
        };
        var conformanceEngine = new ValidationEngine(
            Options.Create(conformanceConfig),
            _blueprintCacheMock.Object,
            _hashProviderMock.Object,
            _cryptoModuleMock.Object,
            _walletUtilitiesMock.Object,
            _registerClientMock.Object,
            _rightsEnforcementMock.Object,
            _loggerMock.Object);

        var tx = CreateValidTransaction(previousTransactionId: null);
        var blueprint = CreateTestBlueprintWithConformance(tx.BlueprintId, isStartingAction: false);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Setup other validations to pass
        var hashBytes = Convert.FromHexString(tx.PayloadHash);
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(hashBytes);
        _cryptoModuleMock.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await conformanceEngine.ValidateTransactionAsync(tx);

        // Assert — should fail with BP_001
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_001");
    }

    #endregion

    #region Participant Transaction Tests

    [Fact]
    public void ValidateStructure_ParticipantTransaction_NullBlueprintId_ReturnsSuccess()
    {
        // Arrange - participant TX has no blueprint/action context
        var tx = CreateValidTransaction(blueprintId: null, actionId: null);
        tx.Metadata["Type"] = "Participant";

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateStructure_NonParticipantTransaction_EmptyBlueprintId_ReturnsFailed()
    {
        // Arrange - regular TX still requires BlueprintId
        var tx = CreateValidTransaction(blueprintId: "", actionId: "");

        // Act
        var result = _engine.ValidateStructure(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_003");
        result.Errors.Should().Contain(e => e.Code == "VAL_STRUCT_004");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_ValidPayload_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateParticipantTransaction(CreateValidParticipantPayloadJson());

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_MissingRequiredField_ReturnsSchemaError()
    {
        // Arrange - payload missing participantName
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "status": "active",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519"
                }
            ]
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_InvalidStatus_ReturnsSchemaError()
    {
        // Arrange - status must be one of: active, deprecated, revoked
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "invalid",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519"
                }
            ]
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_EmptyAddresses_ReturnsSchemaError()
    {
        // Arrange - addresses must have minItems: 1
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 1,
            "addresses": []
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_InvalidAlgorithm_ReturnsSchemaError()
    {
        // Arrange - algorithm must be one of: ED25519, P-256, RSA-4096
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "SECP256K1"
                }
            ]
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_VersionZero_ReturnsSchemaError()
    {
        // Arrange - version must be >= 1
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 0,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519"
                }
            ]
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_AdditionalProperties_ReturnsSchemaError()
    {
        // Arrange - additionalProperties: false should reject extra fields
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519"
                }
            ],
            "participantType": "human"
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PARTICIPANT_001");
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_DoesNotFetchBlueprint()
    {
        // Arrange
        var tx = CreateParticipantTransaction(CreateValidParticipantPayloadJson());

        // Act
        await _engine.ValidateSchemaAsync(tx);

        // Assert - should never call blueprint cache for participant TXs
        _blueprintCacheMock.Verify(
            c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ParticipantTransaction_ReturnsSuccess()
    {
        // Arrange
        var tx = CreateParticipantTransaction(CreateValidParticipantPayloadJson());

        // Act
        var result = await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBlueprintConformanceAsync_ParticipantTransaction_DoesNotFetchBlueprint()
    {
        // Arrange
        var tx = CreateParticipantTransaction(CreateValidParticipantPayloadJson());

        // Act
        await _engine.ValidateBlueprintConformanceAsync(tx);

        // Assert
        _blueprintCacheMock.Verify(
            c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_MultipleAddresses_ReturnsSuccess()
    {
        // Arrange - multiple addresses within limit (max 10)
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "addr1",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519",
                    "primary": true
                },
                {
                    "walletAddress": "addr2",
                    "publicKey": "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=",
                    "algorithm": "P-256"
                }
            ]
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_WithOptionalMetadata_ReturnsSuccess()
    {
        // Arrange - metadata is optional
        var payloadJson = """
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "active",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519"
                }
            ],
            "metadata": {
                "description": "Primary signing participant",
                "department": "Engineering"
            }
        }
        """;
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_DeprecatedStatus_ReturnsSuccess()
    {
        // Arrange
        var payloadJson = CreateValidParticipantPayloadJson(status: "deprecated");
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchemaAsync_ParticipantTransaction_RevokedStatus_ReturnsSuccess()
    {
        // Arrange
        var payloadJson = CreateValidParticipantPayloadJson(status: "revoked");
        var tx = CreateParticipantTransaction(payloadJson);

        // Act
        var result = await _engine.ValidateSchemaAsync(tx);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    private static Transaction CreateParticipantTransaction(string payloadJson)
    {
        var tx = CreateValidTransaction(
            blueprintId: null,
            actionId: null,
            payloadJson: payloadJson);
        tx.Metadata["Type"] = "Participant";
        return tx;
    }

    private static string CreateValidParticipantPayloadJson(string status = "active")
    {
        return $$"""
        {
            "participantId": "550e8400-e29b-41d4-a716-446655440000",
            "organizationName": "Acme Corp",
            "participantName": "Alice",
            "status": "{{status}}",
            "version": 1,
            "addresses": [
                {
                    "walletAddress": "1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2",
                    "publicKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                    "algorithm": "ED25519",
                    "primary": true
                }
            ]
        }
        """;
    }

    #endregion

    #region Helper Methods

    private static Transaction CreateValidTransaction(
        string? transactionId = null,
        string? registerId = null,
        string? blueprintId = null,
        string? actionId = null,
        string? payloadHash = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        List<Signature>? signatures = null,
        string? previousTransactionId = null,
        string? payloadJson = null)
    {
        var json = payloadJson ?? "{}";
        var defaultPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // SHA256 of {}

        return new Transaction
        {
            TransactionId = transactionId ?? $"tx-{Guid.NewGuid():N}",
            RegisterId = registerId ?? "test-register",
            BlueprintId = blueprintId ?? "bp-1",
            ActionId = actionId ?? "1",
            Payload = JsonSerializer.Deserialize<JsonElement>(json),
            PayloadHash = payloadHash ?? defaultPayloadHash,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt,
            PreviousTransactionId = previousTransactionId,
            Signatures = signatures ??
            [
                new Signature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    private void SetupSuccessfulValidation(Transaction tx)
    {
        // Setup hash provider to return matching hash
        var hashBytes = Convert.FromHexString(tx.PayloadHash);
        _hashProviderMock.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256))
            .Returns(hashBytes);

        // Setup blueprint cache
        var blueprint = CreateTestBlueprint(tx.BlueprintId);
        _blueprintCacheMock.Setup(c => c.GetBlueprintAsync(tx.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);

        // Setup signature verification
        _cryptoModuleMock.Setup(c => c.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.Success);

        // Setup register client for chain validation (empty register = genesis)
        _registerClientMock.Setup(r => r.GetRegisterHeightAsync(tx.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private static BlueprintModel CreateTestBlueprint(string blueprintId)
    {
        return new BlueprintModel
        {
            Id = blueprintId,
            Title = "Test Blueprint",
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant
                {
                    Id = "participant-1",
                    Name = "Test Participant"
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Test Action",
                    Sender = "participant-1"
                }
            ]
        };
    }

    private static BlueprintModel CreateTestBlueprintWithConformance(
        string blueprintId,
        bool isStartingAction = true,
        string? walletAddress = null)
    {
        return new BlueprintModel
        {
            Id = blueprintId,
            Title = "Test Blueprint (Conformance)",
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant
                {
                    Id = "participant-1",
                    Name = "Test Participant",
                    WalletAddress = walletAddress
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Test Action",
                    Sender = "participant-1",
                    IsStartingAction = isStartingAction
                }
            ]
        };
    }

    private static BlueprintModel CreateTestBlueprintWithRoutes(
        string blueprintId,
        int fromActionId,
        int[] toActionIds,
        int? rejectionTargetActionId = null)
    {
        var fromAction = new ActionModel
        {
            Id = fromActionId,
            Title = $"Action {fromActionId}",
            Sender = "participant-1",
            IsStartingAction = true,
            Routes = [new Sorcha.Blueprint.Models.Route { Id = "route-1", NextActionIds = toActionIds }],
            RejectionConfig = rejectionTargetActionId.HasValue
                ? new Sorcha.Blueprint.Models.RejectionConfig { TargetActionId = rejectionTargetActionId.Value }
                : null
        };

        var targetActions = toActionIds
            .Concat(rejectionTargetActionId.HasValue ? [rejectionTargetActionId.Value] : [])
            .Distinct()
            .Select(id => new ActionModel { Id = id, Title = $"Action {id}", Sender = "participant-1" })
            .ToList();

        var allActions = new List<ActionModel> { fromAction };
        allActions.AddRange(targetActions);

        return new BlueprintModel
        {
            Id = blueprintId,
            Title = "Test Blueprint (Routes)",
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant
                {
                    Id = "participant-1",
                    Name = "Test Participant"
                }
            ],
            Actions = allActions
        };
    }

    private static BlueprintModel CreateTestBlueprintWithSchema(string blueprintId, string? schemaJson = null)
    {
        var defaultSchema = """
        {
            "type": "object",
            "required": ["name", "amount"],
            "properties": {
                "name": { "type": "string" },
                "amount": { "type": "number" }
            }
        }
        """;

        var schema = schemaJson ?? defaultSchema;

        return new BlueprintModel
        {
            Id = blueprintId,
            Title = "Test Blueprint With Schema",
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant
                {
                    Id = "participant-1",
                    Name = "Test Participant"
                }
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Test Action",
                    Sender = "participant-1",
                    DataSchemas = [JsonDocument.Parse(schema)]
                }
            ]
        };
    }

    #endregion
}
