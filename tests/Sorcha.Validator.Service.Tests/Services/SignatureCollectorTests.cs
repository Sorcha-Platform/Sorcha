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

public class SignatureCollectorTests
{
    private readonly Mock<IOptions<ValidatorConfiguration>> _validatorConfigMock;
    private readonly Mock<IOptions<ConsensusConfiguration>> _consensusConfigMock;
    private readonly Mock<ILeaderElectionService> _leaderElectionMock;
    private readonly Mock<ILogger<SignatureCollector>> _loggerMock;
    private readonly SignatureCollector _collector;

    public SignatureCollectorTests()
    {
        _validatorConfigMock = new Mock<IOptions<ValidatorConfiguration>>();
        _consensusConfigMock = new Mock<IOptions<ConsensusConfiguration>>();
        _leaderElectionMock = new Mock<ILeaderElectionService>();
        _loggerMock = new Mock<ILogger<SignatureCollector>>();

        _validatorConfigMock.Setup(x => x.Value).Returns(new ValidatorConfiguration
        {
            ValidatorId = "validator-1",
            SystemWalletAddress = "system-wallet-1"
        });

        _consensusConfigMock.Setup(x => x.Value).Returns(new ConsensusConfiguration
        {
            VoteTimeout = TimeSpan.FromSeconds(5),
            MaxRetries = 3,
            ApprovalThreshold = 0.67
        });

        _leaderElectionMock.Setup(x => x.CurrentTerm).Returns(1);

        _collector = new SignatureCollector(
            _validatorConfigMock.Object,
            _consensusConfigMock.Object,
            _leaderElectionMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullValidatorConfig_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SignatureCollector(
            null!,
            _consensusConfigMock.Object,
            _leaderElectionMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validatorConfig");
    }

    [Fact]
    public void Constructor_NullConsensusConfig_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SignatureCollector(
            _validatorConfigMock.Object,
            null!,
            _leaderElectionMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("consensusConfig");
    }

    [Fact]
    public void Constructor_NullLeaderElection_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SignatureCollector(
            _validatorConfigMock.Object,
            _consensusConfigMock.Object,
            null!,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("leaderElection");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SignatureCollector(
            _validatorConfigMock.Object,
            _consensusConfigMock.Object,
            _leaderElectionMock.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region CollectSignaturesAsync Tests

    [Fact]
    public async Task CollectSignaturesAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Arrange
        var config = CreateConsensusConfig();
        var validators = new List<ValidatorInfo>();

        // Act
        var act = () => _collector.CollectSignaturesAsync(null!, config, validators);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task CollectSignaturesAsync_NullConfig_ThrowsArgumentNullException()
    {
        // Arrange
        var docket = CreateDocket();
        var validators = new List<ValidatorInfo>();

        // Act
        var act = () => _collector.CollectSignaturesAsync(docket, null!, validators);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("config");
    }

    [Fact]
    public async Task CollectSignaturesAsync_NullValidators_ThrowsArgumentNullException()
    {
        // Arrange
        var docket = CreateDocket();
        var config = CreateConsensusConfig();

        // Act
        var act = () => _collector.CollectSignaturesAsync(docket, config, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("validators");
    }

    [Fact]
    public async Task CollectSignaturesAsync_WithProposerSignature_IncludesInResult()
    {
        // Arrange
        var docket = CreateDocket();
        var config = CreateConsensusConfig(minThreshold: 1);
        var validators = new List<ValidatorInfo> { CreateValidatorInfo("validator-1") };

        // Act
        var result = await _collector.CollectSignaturesAsync(docket, config, validators);

        // Assert
        result.Signatures.Should().Contain(s => s.IsInitiator);
        result.Approvals.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CollectSignaturesAsync_NoOtherValidators_ReturnsImmediately()
    {
        // Arrange
        var docket = CreateDocket();
        var config = CreateConsensusConfig(minThreshold: 1);
        var validators = new List<ValidatorInfo> { CreateValidatorInfo("validator-1") };

        // Act
        var result = await _collector.CollectSignaturesAsync(docket, config, validators);

        // Assert
        result.Should().NotBeNull();
        result.TotalValidators.Should().Be(1);
    }

    [Fact]
    public async Task CollectSignaturesAsync_MultipleValidators_TracksNonResponders()
    {
        // Arrange — other validators are unreachable (no gRPC server)
        var docket = CreateDocket();
        var config = CreateConsensusConfig(minThreshold: 2);
        var validators = new List<ValidatorInfo>
        {
            CreateValidatorInfo("validator-1"),
            CreateValidatorInfo("validator-2"),
            CreateValidatorInfo("validator-3")
        };

        // Act
        var result = await _collector.CollectSignaturesAsync(docket, config, validators);

        // Assert
        result.TotalValidators.Should().Be(3);
        // Only self (validator-1) approves via proposer signature; others fail gRPC connection
        result.Approvals.Should().BeGreaterThanOrEqualTo(1);
        result.NonResponders.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CollectSignaturesAsync_OnlySelfAvailable_ThresholdMetWithMin1()
    {
        // Arrange — min threshold of 1, only self in validator list
        var docket = CreateDocket();
        var config = CreateConsensusConfig(minThreshold: 1, maxSignatures: 5);
        var validators = new List<ValidatorInfo>
        {
            CreateValidatorInfo("validator-1")
        };

        // Act
        var result = await _collector.CollectSignaturesAsync(docket, config, validators);

        // Assert
        result.ThresholdMet.Should().BeTrue();
    }

    [Fact]
    public async Task CollectSignaturesAsync_RecordsDuration()
    {
        // Arrange
        var docket = CreateDocket();
        var config = CreateConsensusConfig();
        var validators = new List<ValidatorInfo> { CreateValidatorInfo("validator-1") };

        // Act
        var result = await _collector.CollectSignaturesAsync(docket, config, validators);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region RequestSignatureAsync Tests

    [Fact]
    public async Task RequestSignatureAsync_NullValidator_ThrowsArgumentNullException()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        var act = () => _collector.RequestSignatureAsync(null!, docket, 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("validator");
    }

    [Fact]
    public async Task RequestSignatureAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Arrange
        var validator = CreateValidatorInfo("validator-1");

        // Act
        var act = () => _collector.RequestSignatureAsync(validator, null!, 1);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task RequestSignatureAsync_UnreachableEndpoint_ReturnsNull()
    {
        // Arrange — gRPC endpoint is unreachable (no server running)
        var validator = CreateValidatorInfo("validator-2");
        var docket = CreateDocket();

        // Act — should return null because the gRPC connection will fail
        var response = await _collector.RequestSignatureAsync(validator, docket, 1);

        // Assert — unreachable validators return null (caught by error handler)
        response.Should().BeNull();
    }

    [Fact]
    public async Task RequestSignatureAsync_Cancelled_ReturnsNullOrThrows()
    {
        // Arrange
        var validator = CreateValidatorInfo("validator-2");
        var docket = CreateDocket();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act — pre-cancelled token; may throw OperationCancelled or return null
        try
        {
            var response = await _collector.RequestSignatureAsync(validator, docket, 1, cts.Token);
            // If it doesn't throw, response should be null (connection failed)
            response.Should().BeNull();
        }
        catch (OperationCanceledException)
        {
            // Expected behavior — cancellation propagated
        }
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _collector.GetStats();

        // Assert
        stats.TotalCollections.Should().Be(0);
        stats.SuccessfulCollections.Should().Be(0);
        stats.FailedCollections.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterSuccessfulCollection_IncrementsCounters()
    {
        // Arrange
        var docket = CreateDocket();
        var config = CreateConsensusConfig(minThreshold: 1);
        var validators = new List<ValidatorInfo> { CreateValidatorInfo("validator-1") };

        // Act
        await _collector.CollectSignaturesAsync(docket, config, validators);
        var stats = _collector.GetStats();

        // Assert
        stats.TotalCollections.Should().Be(1);
    }

    [Fact]
    public void GetStats_SuccessRate_CalculatesCorrectly()
    {
        // Arrange - initial state has 0 collections
        var stats = _collector.GetStats();

        // Assert
        stats.SuccessRate.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private static Docket CreateDocket()
    {
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
            Transactions = []
        };
    }

    private static ValidatorInfo CreateValidatorInfo(string validatorId)
    {
        return new ValidatorInfo
        {
            ValidatorId = validatorId,
            PublicKey = $"pubkey-{validatorId}",
            GrpcEndpoint = $"https://{validatorId}:5000",
            RegisteredAt = DateTimeOffset.UtcNow,
            Status = ValidatorStatusEnum.Active
        };
    }

    private static ConsensusConfig CreateConsensusConfig(
        int minThreshold = 2,
        int maxThreshold = 10,
        int maxSignatures = 10)
    {
        return new ConsensusConfig
        {
            SignatureThresholdMin = minThreshold,
            SignatureThresholdMax = maxThreshold,
            MaxSignaturesPerDocket = maxSignatures,
            MaxTransactionsPerDocket = 100,
            DocketTimeout = TimeSpan.FromSeconds(5),
            DocketBuildInterval = TimeSpan.FromSeconds(10)
        };
    }

    #endregion
}
