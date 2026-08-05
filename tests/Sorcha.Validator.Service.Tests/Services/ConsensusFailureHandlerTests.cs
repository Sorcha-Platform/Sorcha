// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using ValidatorStatusEnum = Sorcha.Validator.Service.Services.Interfaces.ValidatorStatus;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

public class ConsensusFailureHandlerTests
{
    private readonly Mock<IOptions<ConsensusConfiguration>> _consensusConfigMock;
    private readonly Mock<ISignatureCollector> _signatureCollectorMock;
    private readonly Mock<IValidatorRegistry> _validatorRegistryMock;
    private readonly Mock<IGenesisConfigService> _genesisConfigServiceMock;
    private readonly Mock<IMemPoolManager> _memPoolManagerMock;
    private readonly Mock<IPendingDocketStore> _pendingDocketStoreMock;
    private readonly Mock<ILogger<ConsensusFailureHandler>> _loggerMock;
    private readonly ConsensusFailureHandler _handler;

    public ConsensusFailureHandlerTests()
    {
        _consensusConfigMock = new Mock<IOptions<ConsensusConfiguration>>();
        _signatureCollectorMock = new Mock<ISignatureCollector>();
        _validatorRegistryMock = new Mock<IValidatorRegistry>();
        _genesisConfigServiceMock = new Mock<IGenesisConfigService>();
        _memPoolManagerMock = new Mock<IMemPoolManager>();
        _pendingDocketStoreMock = new Mock<IPendingDocketStore>();
        _loggerMock = new Mock<ILogger<ConsensusFailureHandler>>();

        _consensusConfigMock.Setup(x => x.Value).Returns(new ConsensusConfiguration
        {
            VoteTimeout = TimeSpan.FromSeconds(5),
            MaxRetries = 3,
            ApprovalThreshold = 0.67
        });

        _handler = new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            _signatureCollectorMock.Object,
            _validatorRegistryMock.Object,
            _genesisConfigServiceMock.Object,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullConsensusConfig_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            null!,
            _signatureCollectorMock.Object,
            _validatorRegistryMock.Object,
            _genesisConfigServiceMock.Object,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("consensusConfig");
    }

    [Fact]
    public void Constructor_NullSignatureCollector_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            null!,
            _validatorRegistryMock.Object,
            _genesisConfigServiceMock.Object,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("signatureCollector");
    }

    [Fact]
    public void Constructor_NullValidatorRegistry_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            _signatureCollectorMock.Object,
            null!,
            _genesisConfigServiceMock.Object,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validatorRegistry");
    }

    [Fact]
    public void Constructor_NullGenesisConfigService_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            _signatureCollectorMock.Object,
            _validatorRegistryMock.Object,
            null!,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("genesisConfigService");
    }

    [Fact]
    public void Constructor_NullMemPoolManager_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            _signatureCollectorMock.Object,
            _validatorRegistryMock.Object,
            _genesisConfigServiceMock.Object,
            null!,
            _pendingDocketStoreMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("memPoolManager");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new ConsensusFailureHandler(
            _consensusConfigMock.Object,
            _signatureCollectorMock.Object,
            _validatorRegistryMock.Object,
            _genesisConfigServiceMock.Object,
            _memPoolManagerMock.Object,
            _pendingDocketStoreMock.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region HandleFailureAsync Tests

    [Fact]
    public async Task HandleFailureAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Arrange
        var result = CreateSignatureCollectionResult(thresholdMet: false);

        // Act
        var act = () => _handler.HandleFailureAsync(null!, result);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task HandleFailureAsync_NullResult_ThrowsArgumentNullException()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        var act = () => _handler.HandleFailureAsync(docket, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("result");
    }

    [Fact]
    public async Task HandleFailureAsync_ThresholdAlreadyMet_ReturnsNoActionNeeded()
    {
        // Arrange
        var docket = CreateDocket();
        var result = CreateSignatureCollectionResult(thresholdMet: true);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, result);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.NoActionNeeded);
        recovery.Succeeded.Should().BeTrue();
        recovery.RetryAttempts.Should().Be(0);
    }

    [Fact]
    public async Task HandleFailureAsync_MaxRetriesExceeded_ReturnsAbandon()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 5);
        var result = CreateSignatureCollectionResult(thresholdMet: false);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, result);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Abandon);
        recovery.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleFailureAsync_MaxRetriesExceeded_ReturnsTransactionsToPool()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 5, transactionCount: 3);
        var result = CreateSignatureCollectionResult(thresholdMet: false);

        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(),
                It.IsAny<List<Transaction>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, result);

        // Assert
        recovery.TransactionsReturnedToPool.Should().Be(3);
        _memPoolManagerMock.Verify(
            x => x.ReturnTransactionsAsync(
                docket.RegisterId,
                It.IsAny<List<Transaction>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFailureAsync_FirstRetry_AttemptsRetry()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);

        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.RetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailureAsync_RetrySucceeds_ReturnsSuccess()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);

        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.Succeeded.Should().BeTrue();
        recovery.UpdatedDocket.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleFailureAsync_RetryFails_ReturnsFailedRetry()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false, approvals: 1);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: false, approvals: 2);

        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.Succeeded.Should().BeFalse();
        recovery.RetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailureAsync_RecordsRecoveryDuration()
    {
        // Arrange
        var docket = CreateDocket();
        var result = CreateSignatureCollectionResult(thresholdMet: true);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, result);

        // Assert
        recovery.RecoveryDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region AbandonDocketAsync Tests

    [Fact]
    public async Task AbandonDocketAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _handler.AbandonDocketAsync(null!, "test reason");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task AbandonDocketAsync_NullReason_ThrowsArgumentException()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        var act = () => _handler.AbandonDocketAsync(docket, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("reason");
    }

    [Fact]
    public async Task AbandonDocketAsync_EmptyReason_ThrowsArgumentException()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        var act = () => _handler.AbandonDocketAsync(docket, "  ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("reason");
    }

    [Fact]
    public async Task AbandonDocketAsync_ValidInput_SetsDocketStatusToRejected()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _handler.AbandonDocketAsync(docket, "Max retries exceeded");

        // Assert
        docket.Status.Should().Be(DocketStatus.Rejected);
    }

    #endregion

    #region ReturnTransactionsToPoolAsync Tests

    [Fact]
    public async Task ReturnTransactionsToPoolAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _handler.ReturnTransactionsToPoolAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task ReturnTransactionsToPoolAsync_NoTransactions_ReturnsZero()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 0);

        // Act
        var count = await _handler.ReturnTransactionsToPoolAsync(docket);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task ReturnTransactionsToPoolAsync_WithTransactions_ReturnsAll()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 5);

        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(),
                It.IsAny<List<Transaction>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var count = await _handler.ReturnTransactionsToPoolAsync(docket);

        // Assert
        count.Should().Be(5);
    }

    [Fact]
    public async Task ReturnTransactionsToPoolAsync_PoolFails_ReturnsZero()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 3);

        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(),
                It.IsAny<List<Transaction>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Pool error"));

        // Act
        var count = await _handler.ReturnTransactionsToPoolAsync(docket);

        // Assert
        count.Should().Be(0);
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _handler.GetStats();

        // Assert
        stats.TotalFailures.Should().Be(0);
        stats.SuccessfulRecoveries.Should().Be(0);
        stats.DocketsAbandoned.Should().Be(0);
        stats.TotalRetryAttempts.Should().Be(0);
        stats.TransactionsReturnedToPool.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterFailure_IncrementsTotalFailures()
    {
        // Arrange
        var docket = CreateDocket();
        var result = CreateSignatureCollectionResult(thresholdMet: true);

        // Act
        await _handler.HandleFailureAsync(docket, result);
        var stats = _handler.GetStats();

        // Assert
        stats.TotalFailures.Should().Be(1);
    }

    [Fact]
    public void GetStats_RecoveryRate_ReturnsZeroWhenNoFailures()
    {
        // Act
        var stats = _handler.GetStats();

        // Assert
        stats.RecoveryRate.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterSuccessfulRecovery_IncrementsSuccessfulRecoveries()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        await _handler.HandleFailureAsync(docket, failedResult);
        var stats = _handler.GetStats();

        // Assert
        stats.SuccessfulRecoveries.Should().Be(1);
        stats.TotalRetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_AfterAbandon_IncrementsDocketsAbandoned()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 5, transactionCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);

        // Act
        await _handler.HandleFailureAsync(docket, failedResult);
        var stats = _handler.GetStats();

        // Assert
        stats.DocketsAbandoned.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_AfterReturnTransactions_IncrementsTransactionsReturnedToPool()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 4);
        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(), It.IsAny<List<Transaction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.ReturnTransactionsToPoolAsync(docket);
        var stats = _handler.GetStats();

        // Assert
        stats.TransactionsReturnedToPool.Should().Be(4);
    }

    [Fact]
    public async Task GetStats_RecoveryRate_CalculatesCorrectly()
    {
        // Arrange - 2 failures: 1 successful recovery, 1 threshold-already-met (no recovery counted)
        var retrySuccess = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retrySuccess);

        // Act - first call: retry succeeds (1 failure, 1 recovery)
        var docket1 = CreateDocket(retryCount: 0);
        await _handler.HandleFailureAsync(docket1, CreateSignatureCollectionResult(thresholdMet: false));

        // second call: threshold already met (1 failure, 0 recovery)
        var docket2 = CreateDocket();
        await _handler.HandleFailureAsync(docket2, CreateSignatureCollectionResult(thresholdMet: true));

        var stats = _handler.GetStats();

        // Assert - 1 recovery out of 2 total failures = 0.5
        stats.TotalFailures.Should().Be(2);
        stats.SuccessfulRecoveries.Should().Be(1);
        stats.RecoveryRate.Should().Be(0.5);
    }

    #endregion

    #region Timeout Failure Tests

    [Fact]
    public async Task HandleFailureAsync_TimedOutResult_AttemptsRetry()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var timedOutResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 1, totalValidators: 5, timedOut: true);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, timedOutResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.Succeeded.Should().BeTrue();
        recovery.RetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailureAsync_TimedOutAtMaxRetries_Abandons()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 3);
        var timedOutResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 1, totalValidators: 5, timedOut: true);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, timedOutResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Abandon);
        recovery.Succeeded.Should().BeFalse();
        recovery.Reason.Should().Contain("Max retries exceeded");
    }

    #endregion

    #region Quorum Not Reached Tests

    [Fact]
    public async Task HandleFailureAsync_QuorumNotReached_AttemptsRetry()
    {
        // Arrange - only 1 of 10 validators responded
        var docket = CreateDocket(retryCount: 0);
        var lowQuorumResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 1, totalValidators: 10);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: false, approvals: 2, totalValidators: 10);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, lowQuorumResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.RetryAttempts.Should().Be(1);
        _signatureCollectorMock.Verify(
            x => x.CollectSignaturesAsync(
                It.IsAny<Docket>(),
                It.IsAny<ConsensusConfig>(),
                It.IsAny<IReadOnlyList<ValidatorInfo>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFailureAsync_ZeroApprovalsZeroValidators_AttemptsRetry()
    {
        // Arrange - edge case: no validators available
        var docket = CreateDocket(retryCount: 0);
        var noValidatorsResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 0, totalValidators: 0);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: false, approvals: 0, totalValidators: 0);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, noValidatorsResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Conflicting Votes Tests

    [Fact]
    public async Task HandleFailureAsync_WithRejections_AttemptsRetry()
    {
        // Arrange - some validators rejected the docket
        var docket = CreateDocket(retryCount: 0);
        var conflictResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 2, totalValidators: 5,
            rejections: 3, rejectionDetails: new Dictionary<string, string>
            {
                ["validator-2"] = "Invalid merkle root",
                ["validator-3"] = "Stale docket",
                ["validator-4"] = "Conflicting transaction"
            });
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, conflictResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.RetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailureAsync_AllRejectionsAtMaxRetries_AbandonsDocket()
    {
        // Arrange - all validators rejected, max retries exceeded
        var docket = CreateDocket(retryCount: 3, transactionCount: 2);
        var allRejectedResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 0, totalValidators: 5,
            rejections: 5, rejectionDetails: new Dictionary<string, string>
            {
                ["validator-1"] = "Invalid signature",
                ["validator-2"] = "Invalid signature",
                ["validator-3"] = "Invalid signature",
                ["validator-4"] = "Invalid signature",
                ["validator-5"] = "Invalid signature"
            });

        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(), It.IsAny<List<Transaction>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, allRejectedResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Abandon);
        recovery.Succeeded.Should().BeFalse();
        recovery.TransactionsReturnedToPool.Should().Be(2);
    }

    #endregion

    #region Invalid Signatures in Votes Tests

    [Fact]
    public async Task HandleFailureAsync_InvalidSignatureRejections_RetryWithFreshValidators()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 1);
        var invalidSigResult = CreateSignatureCollectionResult(
            thresholdMet: false, approvals: 1, totalValidators: 3,
            rejections: 2, rejectionDetails: new Dictionary<string, string>
            {
                ["validator-2"] = "Invalid signature",
                ["validator-3"] = "Signature verification failed"
            });
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, invalidSigResult);

        // Assert
        recovery.Succeeded.Should().BeTrue();
        // Verify fresh validators were fetched
        _validatorRegistryMock.Verify(
            x => x.GetActiveValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Transient Network Failure Recovery Tests

    [Fact]
    public async Task HandleFailureAsync_NetworkFailureOnRetry_PropagatesException()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);

        _validatorRegistryMock
            .Setup(x => x.GetActiveValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network unreachable"));

        // Act & Assert - the exception propagates (not swallowed)
        var act = () => _handler.HandleFailureAsync(docket, failedResult);
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Network unreachable");
    }

    [Fact]
    public async Task HandleFailureAsync_GenesisConfigFails_PropagatesException()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);

        _validatorRegistryMock
            .Setup(x => x.GetActiveValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>());

        _genesisConfigServiceMock
            .Setup(x => x.GetConsensusConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Service unavailable"));

        // Act & Assert
        var act = () => _handler.HandleFailureAsync(docket, failedResult);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task HandleFailureAsync_SignatureCollectorFails_PropagatesException()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);

        _validatorRegistryMock
            .Setup(x => x.GetActiveValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>());

        _genesisConfigServiceMock
            .Setup(x => x.GetConsensusConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsensusConfig
            {
                SignatureThresholdMin = 1, SignatureThresholdMax = 5,
                MaxSignaturesPerDocket = 10, MaxTransactionsPerDocket = 100,
                DocketTimeout = TimeSpan.FromSeconds(5), DocketBuildInterval = TimeSpan.FromSeconds(10)
            });

        _signatureCollectorMock
            .Setup(x => x.CollectSignaturesAsync(
                It.IsAny<Docket>(), It.IsAny<ConsensusConfig>(),
                It.IsAny<IReadOnlyList<ValidatorInfo>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Connection reset"));

        // Act & Assert
        var act = () => _handler.HandleFailureAsync(docket, failedResult);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Multiple Consecutive Failures Tests

    [Fact]
    public async Task HandleFailureAsync_MultipleConsecutiveFailures_AccumulatesStats()
    {
        // Arrange
        var retryFailResult = CreateSignatureCollectionResult(thresholdMet: false);
        SetupRetryMocks(retryFailResult);

        // Act - 3 consecutive failures
        for (var i = 0; i < 3; i++)
        {
            var docket = CreateDocket(retryCount: 0);
            var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
            await _handler.HandleFailureAsync(docket, failedResult);
        }

        var stats = _handler.GetStats();

        // Assert
        stats.TotalFailures.Should().Be(3);
        stats.TotalRetryAttempts.Should().Be(3);
        stats.SuccessfulRecoveries.Should().Be(0);
    }

    [Fact]
    public async Task HandleFailureAsync_MixedResults_TracksAllStats()
    {
        // Act - 1: retry succeeds
        var retrySuccess = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retrySuccess);
        var docket1 = CreateDocket(retryCount: 0);
        await _handler.HandleFailureAsync(docket1, CreateSignatureCollectionResult(thresholdMet: false));

        // 2: threshold already met
        var docket2 = CreateDocket();
        await _handler.HandleFailureAsync(docket2, CreateSignatureCollectionResult(thresholdMet: true));

        // 3: abandon (max retries exceeded)
        var docket3 = CreateDocket(retryCount: 5);
        await _handler.HandleFailureAsync(docket3, CreateSignatureCollectionResult(thresholdMet: false));

        // 4: retry fails
        var retryFail = CreateSignatureCollectionResult(thresholdMet: false);
        SetupRetryMocks(retryFail);
        var docket4 = CreateDocket(retryCount: 0);
        await _handler.HandleFailureAsync(docket4, CreateSignatureCollectionResult(thresholdMet: false));

        var stats = _handler.GetStats();

        // Assert
        stats.TotalFailures.Should().Be(4);
        stats.SuccessfulRecoveries.Should().Be(1);
        stats.DocketsAbandoned.Should().Be(1);
        stats.TotalRetryAttempts.Should().Be(2); // retries for calls 1 and 4
    }

    #endregion

    #region Metadata Edge Cases Tests

    [Fact]
    public async Task HandleFailureAsync_DocketWithEmptyMetadata_TreatsAsFirstAttempt()
    {
        // Arrange - docket with empty metadata (no retryCount key)
        var docket = CreateDocketWithMetadata(new Dictionary<string, string>());
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.RetryAttempts.Should().Be(1);
        recovery.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleFailureAsync_DocketWithInvalidRetryCountMetadata_TreatsAsZero()
    {
        // Arrange
        var docket = CreateDocketWithMetadata(new Dictionary<string, string> { ["retryCount"] = "not-a-number" });
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert - treats invalid retryCount as 0, so does retry
        recovery.RetryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task HandleFailureAsync_DocketWithExactMaxRetries_Abandons()
    {
        // Arrange - retryCount == MaxRetries (3)
        var docket = CreateDocket(retryCount: 3);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Abandon);
    }

    [Fact]
    public async Task HandleFailureAsync_DocketWithRetryCountJustBelowMax_AttemptsRetry()
    {
        // Arrange - retryCount == MaxRetries - 1 (2)
        var docket = CreateDocket(retryCount: 2);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: false);
        SetupRetryMocks(retryResult);

        // Act
        var recovery = await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        recovery.Action.Should().Be(ConsensusRecoveryAction.Retry);
        recovery.RetryAttempts.Should().Be(3);
    }

    #endregion

    #region Logging Verification Tests

    [Fact]
    public async Task HandleFailureAsync_FailedConsensus_LogsWarning()
    {
        // Arrange
        var docket = CreateDocket();
        var result = CreateSignatureCollectionResult(thresholdMet: true);

        // Act
        await _handler.HandleFailureAsync(docket, result);

        // Assert - verify warning was logged about the failure
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Handling consensus failure")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFailureAsync_ThresholdAlreadyMet_LogsInformation()
    {
        // Arrange
        var docket = CreateDocket();
        var result = CreateSignatureCollectionResult(thresholdMet: true);

        // Act
        await _handler.HandleFailureAsync(docket, result);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("no recovery needed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFailureAsync_MaxRetriesExceeded_LogsError()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 5);
        var result = CreateSignatureCollectionResult(thresholdMet: false);

        // Act
        await _handler.HandleFailureAsync(docket, result);

        // Assert - verify the specific "Max retries exceeded, abandoning" log message
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Max retries") && v.ToString()!.Contains("abandoning")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AbandonDocketAsync_ValidInput_LogsError()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _handler.AbandonDocketAsync(docket, "Test abandonment reason");

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Abandoning docket")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AbandonDocketAsync_PersistFails_LogsWarningButDoesNotThrow()
    {
        // Arrange
        var docket = CreateDocket();
        _pendingDocketStoreMock
            .Setup(x => x.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<DocketStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database unavailable"));

        // Act - should not throw
        await _handler.AbandonDocketAsync(docket, "Test reason");

        // Assert - status still updated in memory
        docket.Status.Should().Be(DocketStatus.Rejected);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to persist rejected status")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ReturnTransactionsToPoolAsync_PoolFails_LogsWarning()
    {
        // Arrange
        var docket = CreateDocket(transactionCount: 2);
        _memPoolManagerMock
            .Setup(x => x.ReturnTransactionsAsync(
                It.IsAny<string>(), It.IsAny<List<Transaction>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Pool error"));

        // Act
        await _handler.ReturnTransactionsToPoolAsync(docket);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to return transactions")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFailureAsync_RetrySucceeds_LogsRecoveryInformation()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var retryResult = CreateSignatureCollectionResult(thresholdMet: true);
        SetupRetryMocks(retryResult);

        // Act
        await _handler.HandleFailureAsync(docket, failedResult);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Recovery succeeded")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task HandleFailureAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var docket = CreateDocket(retryCount: 0);
        var failedResult = CreateSignatureCollectionResult(thresholdMet: false);
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert - Task.Delay should throw on cancelled token
        var act = () => _handler.HandleFailureAsync(docket, failedResult, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region AbandonDocketAsync Persistence Tests

    [Fact]
    public async Task AbandonDocketAsync_PersistsStatusToStore()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _handler.AbandonDocketAsync(docket, "consensus failure");

        // Assert
        _pendingDocketStoreMock.Verify(
            x => x.UpdateStatusAsync(docket.DocketId, DocketStatus.Rejected, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupRetryMocks(SignatureCollectionResult retryResult)
    {
        _validatorRegistryMock
            .Setup(x => x.GetActiveValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>
            {
                new()
                {
                    ValidatorId = "validator-1",
                    PublicKey = "pubkey-1",
                    GrpcEndpoint = "https://validator-1:5000",
                    RegisteredAt = DateTimeOffset.UtcNow,
                    Status = ValidatorStatusEnum.Active
                }
            });

        _genesisConfigServiceMock
            .Setup(x => x.GetConsensusConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsensusConfig
            {
                SignatureThresholdMin = 1,
                SignatureThresholdMax = 5,
                MaxSignaturesPerDocket = 10,
                MaxTransactionsPerDocket = 100,
                DocketTimeout = TimeSpan.FromSeconds(5),
                DocketBuildInterval = TimeSpan.FromSeconds(10)
            });

        _signatureCollectorMock
            .Setup(x => x.CollectSignaturesAsync(
                It.IsAny<Docket>(),
                It.IsAny<ConsensusConfig>(),
                It.IsAny<IReadOnlyList<ValidatorInfo>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(retryResult);
    }

    private static Docket CreateDocketWithMetadata(Dictionary<string, string> metadata, int transactionCount = 0)
    {
        var transactions = new List<Transaction>();
        for (var i = 0; i < transactionCount; i++)
        {
            transactions.Add(CreateTransaction($"tx-{i}"));
        }

        return new Docket
        {
            DocketId = $"docket-{Guid.NewGuid():N}",
            RegisterId = "register-1",
            DocketNumber = 1,
            DocketHash = "test-hash",
            ProposerValidatorId = "validator-1",
            ProposerSignature = new RegisterSignature
            {
                PublicKey = "test-key"u8.ToArray(),
                SignatureValue = "test-sig"u8.ToArray(),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow,
                SignedBy = "validator-1"
            },
            MerkleRoot = "test-merkle-root",
            Status = DocketStatus.Proposed,
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = transactions,
            Metadata = metadata
        };
    }

    private static Docket CreateDocket(int retryCount = 0, int transactionCount = 0)
    {
        var transactions = new List<Transaction>();
        for (var i = 0; i < transactionCount; i++)
        {
            transactions.Add(CreateTransaction($"tx-{i}"));
        }

        var metadata = new Dictionary<string, string>();
        if (retryCount > 0)
        {
            metadata["retryCount"] = retryCount.ToString();
        }

        return new Docket
        {
            DocketId = $"docket-{Guid.NewGuid():N}",
            RegisterId = "register-1",
            DocketNumber = 1,
            DocketHash = "test-hash",
            ProposerValidatorId = "validator-1",
            ProposerSignature = new RegisterSignature
            {
                PublicKey = "test-key"u8.ToArray(),
                SignatureValue = "test-sig"u8.ToArray(),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow,
                SignedBy = "validator-1"
            },
            MerkleRoot = "test-merkle-root",
            Status = DocketStatus.Proposed,
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = transactions,
            Metadata = metadata
        };
    }

    private static Transaction CreateTransaction(string transactionId)
    {
        return new Transaction
        {
            TransactionId = transactionId,
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "action-1",
            Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
            PayloadHash = $"payload-hash-{transactionId}",
            Signatures = new List<RegisterSignature>
            {
                new()
                {
                    PublicKey = "public-key"u8.ToArray(),
                    SignatureValue = "signature-value"u8.ToArray(),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = TransactionPriority.Normal
        };
    }

    private static SignatureCollectionResult CreateSignatureCollectionResult(
        bool thresholdMet,
        int approvals = 3,
        int totalValidators = 5,
        bool timedOut = false,
        int rejections = 0,
        Dictionary<string, string>? rejectionDetails = null)
    {
        return new SignatureCollectionResult
        {
            Signatures = [],
            ThresholdMet = thresholdMet,
            TimedOut = timedOut,
            TotalValidators = totalValidators,
            ResponsesReceived = approvals + rejections,
            Approvals = approvals,
            Rejections = rejections,
            Duration = TimeSpan.FromMilliseconds(100),
            NonResponders = [],
            RejectionDetails = rejectionDetails ?? new Dictionary<string, string>()
        };
    }

    #endregion
}
