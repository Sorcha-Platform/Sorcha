// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Register.Models;
// Both namespaces declare a Docket (#1371); the alias is required, not stylistic.
using Docket = Sorcha.Validator.Service.Models.Docket;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ValidatorOrchestrator
/// Tests cover >85% code coverage as required by project standards
/// </summary>
public class ValidatorOrchestratorTests
{
    private readonly Mock<IVerifiedTransactionQueue> _mockVerifiedQueue;
    private readonly Mock<IDocketBuilder> _mockDocketBuilder;
    private readonly Mock<IConsensusEngine> _mockConsensusEngine;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IPeerServiceClient> _mockPeerClient;
    private readonly Mock<ILogger<ValidatorOrchestrator>> _mockLogger;
    private readonly ValidatorOrchestrator _orchestrator;

    public ValidatorOrchestratorTests()
    {
        _mockVerifiedQueue = new Mock<IVerifiedTransactionQueue>();
        _mockDocketBuilder = new Mock<IDocketBuilder>();
        _mockConsensusEngine = new Mock<IConsensusEngine>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockPeerClient = new Mock<IPeerServiceClient>();
        _mockLogger = new Mock<ILogger<ValidatorOrchestrator>>();

        _orchestrator = new ValidatorOrchestrator(
            _mockVerifiedQueue.Object,
            _mockDocketBuilder.Object,
            _mockConsensusEngine.Object,
            _mockRegisterClient.Object,
            _mockPeerClient.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullVerifiedQueue_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new ValidatorOrchestrator(
            null!,
            _mockDocketBuilder.Object,
            _mockConsensusEngine.Object,
            _mockRegisterClient.Object,
            _mockPeerClient.Object,
            _mockLogger.Object));

        exception.ParamName.Should().Be("verifiedQueue");
    }

    [Fact]
    public void Constructor_WithNullDocketBuilder_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new ValidatorOrchestrator(
            _mockVerifiedQueue.Object,
            null!,
            _mockConsensusEngine.Object,
            _mockRegisterClient.Object,
            _mockPeerClient.Object,
            _mockLogger.Object));

        exception.ParamName.Should().Be("docketBuilder");
    }

    [Fact]
    public void Constructor_WithNullConsensusEngine_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new ValidatorOrchestrator(
            _mockVerifiedQueue.Object,
            _mockDocketBuilder.Object,
            null!,
            _mockRegisterClient.Object,
            _mockPeerClient.Object,
            _mockLogger.Object));

        exception.ParamName.Should().Be("consensusEngine");
    }

    #endregion

    #region StartValidatorAsync Tests

    [Fact]
    public async Task StartValidatorAsync_ForNewRegister_ReturnsTrue()
    {
        // Arrange
        var registerId = "register-1";

        // Act
        var result = await _orchestrator.StartValidatorAsync(registerId);

        // Assert
        result.Should().BeTrue();

        // Verify status was created
        var status = await _orchestrator.GetValidatorStatusAsync(registerId);
        status.Should().NotBeNull();
        status!.RegisterId.Should().Be(registerId);
        status.IsActive.Should().BeTrue();
        status.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StartValidatorAsync_ForAlreadyActiveRegister_ReturnsTrue()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        // Act
        var result = await _orchestrator.StartValidatorAsync(registerId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task StartValidatorAsync_AfterStopping_RestartsValidator()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);
        await _orchestrator.StopValidatorAsync(registerId);

        // Act
        var result = await _orchestrator.StartValidatorAsync(registerId);

        // Assert
        result.Should().BeTrue();

        var status = await _orchestrator.GetValidatorStatusAsync(registerId);
        status!.IsActive.Should().BeTrue();
    }

    #endregion

    #region StopValidatorAsync Tests

    [Fact]
    public async Task StopValidatorAsync_ForActiveValidator_ReturnsTrue()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        // Act
        var result = await _orchestrator.StopValidatorAsync(registerId);

        // Assert
        result.Should().BeTrue();

        var status = await _orchestrator.GetValidatorStatusAsync(registerId);
        status!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task StopValidatorAsync_ForNonExistentValidator_ReturnsFalse()
    {
        // Arrange
        var registerId = "non-existent-register";

        // Act
        var result = await _orchestrator.StopValidatorAsync(registerId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task StopValidatorAsync_WithPersistMemPoolTrue_LogsPersistenceIntent()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        // Act
        var result = await _orchestrator.StopValidatorAsync(registerId, persistMemPool: true);

        // Assert
        result.Should().BeTrue();

        // Verify persistence logging was called (MemPoolManager is Redis-backed)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Redis-persisted")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetValidatorStatusAsync Tests

    [Fact]
    public async Task GetValidatorStatusAsync_ForActiveValidator_ReturnsStatus()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(25);

        // Act
        var status = await _orchestrator.GetValidatorStatusAsync(registerId);

        // Assert
        status.Should().NotBeNull();
        status!.RegisterId.Should().Be(registerId);
        status.IsActive.Should().BeTrue();
        status.TransactionsInMemPool.Should().Be(25);
        status.DocketsProposed.Should().Be(0);
        status.DocketsConfirmed.Should().Be(0);
        status.DocketsRejected.Should().Be(0);
        status.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_ForNonExistentValidator_ReturnsNull()
    {
        // Arrange
        var registerId = "non-existent-register";

        // Act
        var status = await _orchestrator.GetValidatorStatusAsync(registerId);

        // Assert
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetValidatorStatusAsync_AfterProcessingDockets_ReflectsStatistics()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket = CreateTestDocket(registerId, 0);
        var consensusResult = new ConsensusResult
        {
            Achieved = true,
            Docket = docket,
            Votes = new List<ConsensusVote>(),
            TotalValidators = 3,
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        SetupSuccessfulPipeline(registerId, docket, consensusResult);

        // Process one successful docket
        await _orchestrator.ProcessValidationPipelineAsync(registerId);

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(10);

        // Act
        var status = await _orchestrator.GetValidatorStatusAsync(registerId);

        // Assert
        status.Should().NotBeNull();
        status!.DocketsProposed.Should().Be(1);
        status.DocketsConfirmed.Should().Be(1);
        status.DocketsRejected.Should().Be(0);
        status.LastDocketBuildAt.Should().NotBeNull();
    }

    #endregion

    #region ProcessValidationPipelineAsync Tests

    [Fact]
    public async Task ProcessValidationPipelineAsync_WhenValidatorNotStarted_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WhenValidatorInactive_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);
        await _orchestrator.StopValidatorAsync(registerId);

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WhenShouldNotBuildDocket_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        _mockDocketBuilder
            .Setup(b => b.ShouldBuildDocketAsync(registerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WhenDocketBuildFails_ReturnsNull()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        _mockDocketBuilder
            .Setup(b => b.ShouldBuildDocketAsync(registerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDocketBuilder
            .Setup(b => b.BuildDocketAsync(registerId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Docket?)null);

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WithSuccessfulConsensus_ReturnsSuccessResult()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket = CreateTestDocket(registerId, 0);
        var consensusResult = new ConsensusResult
        {
            Achieved = true,
            Docket = docket,
            Votes = new List<ConsensusVote>
            {
                CreateTestVote("validator-1", VoteDecision.Approve),
                CreateTestVote("validator-2", VoteDecision.Approve)
            },
            TotalValidators = 3,
            Duration = TimeSpan.FromSeconds(2),
            CompletedAt = DateTimeOffset.UtcNow
        };

        SetupSuccessfulPipeline(registerId, docket, consensusResult);

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.Docket.Should().Be(docket);
        result.ConsensusAchieved.Should().BeTrue();
        result.WrittenToRegister.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        // Verify docket was updated with consensus information
        docket.Status.Should().Be(DocketStatus.Confirmed);
        docket.ConsensusAchievedAt.Should().NotBeNull();
        docket.Votes.Should().HaveCount(2);

        // Verify transactions were removed from mem pool
        _mockVerifiedQueue.Verify(
            q => q.Remove(registerId, It.IsAny<string>()),
            Times.Exactly(docket.Transactions.Count));

        // Verify docket was broadcast to peers
        _mockPeerClient.Verify(
            p => p.BroadcastConfirmedDocketAsync(registerId, docket.DocketId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WithFailedConsensus_ReturnsFailureResult()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket = CreateTestDocket(registerId, 0);
        var consensusResult = new ConsensusResult
        {
            Achieved = false,
            Docket = docket,
            Votes = new List<ConsensusVote>
            {
                CreateTestVote("validator-1", VoteDecision.Approve),
                CreateTestVote("validator-2", VoteDecision.Reject)
            },
            TotalValidators = 3,
            FailureReason = "Insufficient validator approvals",
            Duration = TimeSpan.FromSeconds(5),
            CompletedAt = DateTimeOffset.UtcNow
        };

        _mockDocketBuilder
            .Setup(b => b.ShouldBuildDocketAsync(registerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDocketBuilder
            .Setup(b => b.BuildDocketAsync(registerId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docket);

        _mockConsensusEngine
            .Setup(c => c.AchieveConsensusAsync(docket, It.IsAny<CancellationToken>()))
            .ReturnsAsync(consensusResult);

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.Docket.Should().Be(docket);
        result.ConsensusAchieved.Should().BeFalse();
        result.WrittenToRegister.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Insufficient validator approvals");

        // Verify transactions were NOT removed from mem pool (consensus failed)
        _mockVerifiedQueue.Verify(
            q => q.Remove(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        // Verify docket was NOT broadcast (consensus failed)
        _mockPeerClient.Verify(
            p => p.BroadcastConfirmedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_WithException_ReturnsErrorResult()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        _mockDocketBuilder
            .Setup(b => b.ShouldBuildDocketAsync(registerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));

        // Act
        var result = await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.ConsensusAchieved.Should().BeFalse();
        result.WrittenToRegister.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Test error");
        result.Docket.DocketNumber.Should().Be(-1); // Error docket
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_UpdatesValidatorStatistics()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket1 = CreateTestDocket(registerId, 0);
        var docket2 = CreateTestDocket(registerId, 1);

        var successResult = new ConsensusResult
        {
            Achieved = true,
            Docket = docket1,
            Votes = new List<ConsensusVote>(),
            TotalValidators = 3,
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var failureResult = new ConsensusResult
        {
            Achieved = false,
            Docket = docket2,
            Votes = new List<ConsensusVote>(),
            TotalValidators = 3,
            FailureReason = "Failed",
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Setup first successful pipeline
        SetupSuccessfulPipeline(registerId, docket1, successResult);
        await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Setup second failed pipeline
        _mockDocketBuilder
            .Setup(b => b.BuildDocketAsync(registerId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docket2);

        _mockConsensusEngine
            .Setup(c => c.AchieveConsensusAsync(docket2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureResult);

        await _orchestrator.ProcessValidationPipelineAsync(registerId);

        _mockVerifiedQueue
            .Setup(q => q.GetCount(registerId))
            .Returns(5);

        // Act
        var status = await _orchestrator.GetValidatorStatusAsync(registerId);

        // Assert
        status.Should().NotBeNull();
        status!.DocketsProposed.Should().Be(2); // Both dockets were proposed
        status.DocketsConfirmed.Should().Be(1); // Only first succeeded
        status.DocketsRejected.Should().Be(1);  // Second failed
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_RemovesTransactionsFromMemPoolOnSuccess()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket = CreateTestDocket(registerId, 0, transactionCount: 5);
        var consensusResult = new ConsensusResult
        {
            Achieved = true,
            Docket = docket,
            Votes = new List<ConsensusVote>(),
            TotalValidators = 3,
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        SetupSuccessfulPipeline(registerId, docket, consensusResult);

        // Act
        await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        _mockVerifiedQueue.Verify(
            q => q.Remove(registerId, It.IsAny<string>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task ProcessValidationPipelineAsync_BroadcastsConfirmedDocketToPeers()
    {
        // Arrange
        var registerId = "register-1";
        await _orchestrator.StartValidatorAsync(registerId);

        var docket = CreateTestDocket(registerId, 0);
        var consensusResult = new ConsensusResult
        {
            Achieved = true,
            Docket = docket,
            Votes = new List<ConsensusVote>(),
            TotalValidators = 3,
            Duration = TimeSpan.FromSeconds(1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        SetupSuccessfulPipeline(registerId, docket, consensusResult);

        // Act
        await _orchestrator.ProcessValidationPipelineAsync(registerId);

        // Assert
        _mockPeerClient.Verify(
            p => p.BroadcastConfirmedDocketAsync(
                registerId,
                docket.DocketId,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test docket
    /// </summary>
    private static Docket CreateTestDocket(string registerId, long docketNumber, int transactionCount = 3)
    {
        var transactions = new List<Transaction>();
        for (int i = 0; i < transactionCount; i++)
        {
            transactions.Add(new Transaction
            {
                TransactionId = $"tx-{i}",
                RegisterId = registerId,
                BlueprintId = "blueprint-1",
                ActionId = "action-1",
                Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
                PayloadHash = $"payload-hash-{i}",
                Signatures = new List<Signature>
                {
                    new Signature
                    {
                        PublicKey = System.Text.Encoding.UTF8.GetBytes("public-key"),
                        SignatureValue = System.Text.Encoding.UTF8.GetBytes("signature-value"),
                        Algorithm = "ED25519",
                        SignedAt = DateTimeOffset.UtcNow
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow,
                Priority = TransactionPriority.Normal
            });
        }

        return new Docket
        {
            DocketId = $"docket-{docketNumber}",
            RegisterId = registerId,
            DocketNumber = docketNumber,
            PreviousHash = docketNumber > 0 ? $"prev-hash-{docketNumber - 1}" : null,
            DocketHash = $"docket-hash-{docketNumber}",
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = transactions,
            Status = DocketStatus.Proposed,
            ProposerValidatorId = "validator-proposer",
            ProposerSignature = new Signature
            {
                PublicKey = System.Text.Encoding.UTF8.GetBytes("proposer-key"),
                SignatureValue = System.Text.Encoding.UTF8.GetBytes("proposer-sig"),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            MerkleRoot = "merkle-root"
        };
    }

    /// <summary>
    /// Creates a test consensus vote
    /// </summary>
    private static ConsensusVote CreateTestVote(string validatorId, VoteDecision decision)
    {
        return new ConsensusVote
        {
            VoteId = Guid.NewGuid().ToString(),
            DocketId = "docket-id",
            ValidatorId = validatorId,
            Decision = decision,
            VotedAt = DateTimeOffset.UtcNow,
            ValidatorSignature = new Signature
            {
                PublicKey = System.Text.Encoding.UTF8.GetBytes($"{validatorId}-key"),
                SignatureValue = System.Text.Encoding.UTF8.GetBytes("vote-signature"),
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            DocketHash = "docket-hash"
        };
    }

    /// <summary>
    /// Sets up mocks for successful pipeline execution
    /// </summary>
    private void SetupSuccessfulPipeline(string registerId, Docket docket, ConsensusResult consensusResult)
    {
        _mockDocketBuilder
            .Setup(b => b.ShouldBuildDocketAsync(registerId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDocketBuilder
            .Setup(b => b.BuildDocketAsync(registerId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(docket);

        _mockConsensusEngine
            .Setup(c => c.AchieveConsensusAsync(docket, It.IsAny<CancellationToken>()))
            .ReturnsAsync(consensusResult);

        _mockRegisterClient
            .Setup(r => r.WriteDocketAsync(It.IsAny<Sorcha.ServiceClients.Register.DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockVerifiedQueue
            .Setup(q => q.Remove(registerId, It.IsAny<string>()))
            .Returns(true);

        _mockPeerClient
            .Setup(p => p.BroadcastConfirmedDocketAsync(registerId, It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion
}
