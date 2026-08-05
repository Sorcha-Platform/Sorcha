// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Utilities;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

public class DocketConfirmerTests
{
    private readonly Mock<IOptions<DocketConfirmerConfiguration>> _configMock;
    private readonly Mock<IValidationEngine> _validationEngineMock;
    private readonly Mock<IWalletIntegrationService> _walletServiceMock;
    private readonly Mock<IValidatorRegistry> _validatorRegistryMock;
    private readonly Mock<ILeaderElectionService> _leaderElectionMock;
    private readonly Mock<IBadActorDetector> _badActorDetectorMock;
    private readonly Mock<MerkleTree> _merkleTreeMock;
    private readonly Mock<DocketHasher> _docketHasherMock;
    private readonly Mock<ILogger<DocketConfirmer>> _loggerMock;
    private readonly DocketConfirmer _confirmer;

    private readonly DocketConfirmerConfiguration _config;

    public DocketConfirmerTests()
    {
        _config = new DocketConfirmerConfiguration
        {
            ValidationTimeout = TimeSpan.FromSeconds(30),
            EnableParallelValidation = true,
            MaxConcurrentValidations = 10,
            VerifyInitiatorSignature = true,
            VerifyMerkleRoot = true,
            VerifyDocketHash = true,
            MaxClockSkew = TimeSpan.FromMinutes(5),
            MaxDocketAge = TimeSpan.FromMinutes(10)
        };

        _configMock = new Mock<IOptions<DocketConfirmerConfiguration>>();
        _configMock.Setup(x => x.Value).Returns(_config);

        _validationEngineMock = new Mock<IValidationEngine>();
        _walletServiceMock = new Mock<IWalletIntegrationService>();
        _validatorRegistryMock = new Mock<IValidatorRegistry>();
        _leaderElectionMock = new Mock<ILeaderElectionService>();
        _badActorDetectorMock = new Mock<IBadActorDetector>();

        // Create mocks with Moq for classes that need hash provider
        var hashProviderMock = new Mock<Sorcha.Cryptography.Interfaces.IHashProvider>();
        hashProviderMock.Setup(x => x.ComputeHash(It.IsAny<byte[]>(), It.IsAny<Sorcha.Cryptography.Enums.HashType>()))
            .Returns<byte[], Sorcha.Cryptography.Enums.HashType>((data, _) =>
                System.Security.Cryptography.SHA256.HashData(data));

        _merkleTreeMock = new Mock<MerkleTree>(hashProviderMock.Object);
        _docketHasherMock = new Mock<DocketHasher>(hashProviderMock.Object);

        _loggerMock = new Mock<ILogger<DocketConfirmer>>();

        // Setup defaults
        _leaderElectionMock.Setup(x => x.CurrentTerm).Returns(1);
        _leaderElectionMock.Setup(x => x.CurrentLeaderId).Returns("leader-1");
        _leaderElectionMock.Setup(x => x.GetLeaderForTerm(It.IsAny<long>())).Returns("leader-1");
        _validatorRegistryMock.Setup(x => x.IsRegisteredAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _confirmer = new DocketConfirmer(
            _configMock.Object,
            _validationEngineMock.Object,
            _walletServiceMock.Object,
            _validatorRegistryMock.Object,
            _leaderElectionMock.Object,
            _badActorDetectorMock.Object,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new DocketConfirmer(
            null!,
            _validationEngineMock.Object,
            _walletServiceMock.Object,
            _validatorRegistryMock.Object,
            _leaderElectionMock.Object,
            _badActorDetectorMock.Object,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("config");
    }

    [Fact]
    public void Constructor_NullValidationEngine_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new DocketConfirmer(
            _configMock.Object,
            null!,
            _walletServiceMock.Object,
            _validatorRegistryMock.Object,
            _leaderElectionMock.Object,
            _badActorDetectorMock.Object,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validationEngine");
    }

    [Fact]
    public void Constructor_NullWalletService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new DocketConfirmer(
            _configMock.Object,
            _validationEngineMock.Object,
            null!,
            _validatorRegistryMock.Object,
            _leaderElectionMock.Object,
            _badActorDetectorMock.Object,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("walletService");
    }

    [Fact]
    public void Constructor_NullValidatorRegistry_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new DocketConfirmer(
            _configMock.Object,
            _validationEngineMock.Object,
            _walletServiceMock.Object,
            null!,
            _leaderElectionMock.Object,
            _badActorDetectorMock.Object,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validatorRegistry");
    }

    [Fact]
    public void Constructor_NullBadActorDetector_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new DocketConfirmer(
            _configMock.Object,
            _validationEngineMock.Object,
            _walletServiceMock.Object,
            _validatorRegistryMock.Object,
            _leaderElectionMock.Object,
            null!,
            _merkleTreeMock.Object,
            _docketHasherMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("badActorDetector");
    }

    #endregion

    #region ConfirmDocketAsync Tests

    [Fact]
    public async Task ConfirmDocketAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = async () => await _confirmer.ConfirmDocketAsync(null!, CreateValidSignature(), 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task ConfirmDocketAsync_NullSignature_ThrowsArgumentNullException()
    {
        // Act
        var act = async () => await _confirmer.ConfirmDocketAsync(CreateValidDocket(), null!, 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("initiatorSignature");
    }

    [Fact]
    public async Task ConfirmDocketAsync_EmptyDocketId_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(docketId: "");

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidDocketStructure);
    }

    [Fact]
    public async Task ConfirmDocketAsync_EmptyRegisterId_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(registerId: "");

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidDocketStructure);
    }

    [Fact]
    public async Task ConfirmDocketAsync_StaleTerm_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket();
        _leaderElectionMock.Setup(x => x.CurrentTerm).Returns(10);

        // Act - use term 5 (more than 1 behind current term of 10)
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 5);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidTerm);
    }

    [Fact]
    public async Task ConfirmDocketAsync_FutureTerm_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket();
        _leaderElectionMock.Setup(x => x.CurrentTerm).Returns(1);

        // Act - use term 10 (more than 1 ahead of current term of 1)
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 10);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidTerm);
    }

    [Fact]
    public async Task ConfirmDocketAsync_UnregisteredValidator_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket();
        _validatorRegistryMock.Setup(x => x.IsRegisteredAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.UnauthorizedInitiator);
    }

    [Fact]
    public async Task ConfirmDocketAsync_WrongLeaderForTerm_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket();
        _leaderElectionMock.Setup(x => x.GetLeaderForTerm(1)).Returns("other-leader");

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.UnauthorizedInitiator);
        _badActorDetectorMock.Verify(x => x.LogLeaderImpersonation(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmDocketAsync_FutureTimestamp_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(createdAt: DateTimeOffset.UtcNow.AddHours(1));

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidDocketStructure);
        result.RejectionDetails.Should().Contain("future");
    }

    [Fact]
    public async Task ConfirmDocketAsync_OldDocket_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(createdAt: DateTimeOffset.UtcNow.AddHours(-1));

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidDocketStructure);
        result.RejectionDetails.Should().Contain("old");
    }

    [Fact]
    public async Task ConfirmDocketAsync_NegativeDocketNumber_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(docketNumber: -1);

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidSequenceNumber);
    }

    [Fact]
    public async Task ConfirmDocketAsync_NonGenesisWithoutPreviousHash_ReturnsRejection()
    {
        // Arrange
        var docket = CreateValidDocket(docketNumber: 5, previousHash: null);

        // Act
        var result = await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);

        // Assert
        result.Confirmed.Should().BeFalse();
        result.RejectionReason.Should().Be(DocketRejectionReason.InvalidDocketStructure);
    }

    #endregion

    #region ValidateTransactionAsync Tests

    [Fact]
    public async Task ValidateTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        // Act
        var act = async () => await _confirmer.ValidateTransactionAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("transaction");
    }

    [Fact]
    public async Task ValidateTransactionAsync_ValidTransaction_ReturnsSuccess()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationEngineResult.Success(transaction.TransactionId, transaction.RegisterId, TimeSpan.FromMilliseconds(10)));

        // Act
        var result = await _confirmer.ValidateTransactionAsync(transaction);

        // Assert
        result.IsValid.Should().BeTrue();
        result.TransactionId.Should().Be(transaction.TransactionId);
        result.Errors.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateTransactionAsync_InvalidTransaction_ReturnsFailure()
    {
        // Arrange
        var transaction = CreateValidTransaction();
        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationEngineResult.Failure(
                transaction.TransactionId,
                transaction.RegisterId,
                TimeSpan.FromMilliseconds(10),
                new ValidationEngineError
                {
                    Code = "TEST_ERROR",
                    Message = "Test validation error",
                    Category = ValidationErrorCategory.Schema
                }));

        // Act
        var result = await _confirmer.ValidateTransactionAsync(transaction);

        // Assert
        result.IsValid.Should().BeFalse();
        result.TransactionId.Should().Be(transaction.TransactionId);
        result.Errors.Should().Contain("Test validation error");
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _confirmer.GetStats();

        // Assert
        stats.TotalConfirmations.Should().Be(0);
        stats.SuccessfulConfirmations.Should().Be(0);
        stats.RejectedConfirmations.Should().Be(0);
        stats.SuccessRate.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterRejection_UpdatesStats()
    {
        // Arrange
        var docket = CreateValidDocket(docketId: "");

        // Act
        await _confirmer.ConfirmDocketAsync(docket, CreateValidSignature(), 1);
        var stats = _confirmer.GetStats();

        // Assert
        stats.TotalConfirmations.Should().Be(1);
        stats.SuccessfulConfirmations.Should().Be(0);
        stats.RejectedConfirmations.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private Docket CreateValidDocket(
        string? docketId = null,
        string? registerId = null,
        long? docketNumber = null,
        string? previousHash = "not-set",
        DateTimeOffset? createdAt = null)
    {
        return new Docket
        {
            DocketId = docketId ?? "docket-123",
            RegisterId = registerId ?? "register-1",
            DocketNumber = docketNumber ?? 0,
            PreviousHash = previousHash == "not-set" ? null : previousHash,
            DocketHash = "abc123",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            Transactions = [],
            Status = DocketStatus.Proposed,
            ProposerValidatorId = "leader-1",
            ProposerSignature = CreateValidSignature(),
            MerkleRoot = "merkle-root-hash"
        };
    }

    private static RegisterSignature CreateValidSignature()
    {
        return new RegisterSignature
        {
            PublicKey = new byte[32],
            SignatureValue = new byte[64],
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow
        };
    }

    private Transaction CreateValidTransaction()
    {
        return new Transaction
        {
            TransactionId = Guid.NewGuid().ToString(),
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "1",
            Payload = JsonSerializer.SerializeToElement(new { data = "test" }),
            PayloadHash = "hash-123",
            Signatures = [CreateValidSignature()],
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
