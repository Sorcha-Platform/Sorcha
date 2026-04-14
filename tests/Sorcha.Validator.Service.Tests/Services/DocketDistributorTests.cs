// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

public class DocketDistributorTests
{
    private readonly Mock<IPeerServiceClient> _peerClientMock;
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IReceiptGenerator> _receiptGeneratorMock;
    private readonly Mock<IOptions<DocketDistributorConfiguration>> _configMock;
    private readonly Mock<ILogger<DocketDistributor>> _loggerMock;
    private readonly DocketDistributorConfiguration _config;
    private readonly DocketDistributor _distributor;

    public DocketDistributorTests()
    {
        _config = new DocketDistributorConfiguration
        {
            BroadcastTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 3,
            AutoSubmitToRegisterService = true
        };

        _peerClientMock = new Mock<IPeerServiceClient>();
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _receiptGeneratorMock = new Mock<IReceiptGenerator>();
        _configMock = new Mock<IOptions<DocketDistributorConfiguration>>();
        _configMock.Setup(x => x.Value).Returns(_config);
        _loggerMock = new Mock<ILogger<DocketDistributor>>();

        _distributor = new DocketDistributor(
            _peerClientMock.Object,
            _registerClientMock.Object,
            _receiptGeneratorMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullPeerClient_ThrowsArgumentNullException()
    {
        var act = () => new DocketDistributor(
            null!,
            _registerClientMock.Object,
            _receiptGeneratorMock.Object,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("peerClient");
    }

    [Fact]
    public void Constructor_NullRegisterClient_ThrowsArgumentNullException()
    {
        var act = () => new DocketDistributor(
            _peerClientMock.Object,
            null!,
            _receiptGeneratorMock.Object,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("registerClient");
    }

    [Fact]
    public void Constructor_NullReceiptGenerator_ThrowsArgumentNullException()
    {
        var act = () => new DocketDistributor(
            _peerClientMock.Object,
            _registerClientMock.Object,
            null!,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("receiptGenerator");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new DocketDistributor(
            _peerClientMock.Object,
            _registerClientMock.Object,
            _receiptGeneratorMock.Object,
            _configMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullConfig_UsesDefaults()
    {
        // Arrange
        var nullConfigMock = new Mock<IOptions<DocketDistributorConfiguration>>();
        nullConfigMock.Setup(x => x.Value).Returns((DocketDistributorConfiguration)null!);

        // Act - should not throw, uses default config
        var act = () => new DocketDistributor(
            _peerClientMock.Object,
            _registerClientMock.Object,
            _receiptGeneratorMock.Object,
            nullConfigMock.Object,
            _loggerMock.Object);

        act.Should().NotThrow();
    }

    #endregion

    #region BroadcastProposedDocketAsync Tests

    [Fact]
    public async Task BroadcastProposedDocketAsync_ValidDocket_BroadcastsToPeers()
    {
        // Arrange
        var docket = CreateValidDocket();
        var validators = CreateValidatorList(2);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act
        var peerCount = await _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        peerCount.Should().Be(2);
        _peerClientMock.Verify(
            x => x.PublishProposedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_NoPeers_ReturnsZero()
    {
        // Arrange
        var docket = CreateValidDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>());

        // Act
        var peerCount = await _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        peerCount.Should().Be(0);
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_NoPeers_DoesNotCallPublish()
    {
        // Arrange
        var docket = CreateValidDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>());

        // Act
        await _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        _peerClientMock.Verify(
            x => x.PublishProposedDocketAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _distributor.BroadcastProposedDocketAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_PeerQueryFails_PropagatesException()
    {
        // Arrange
        var docket = CreateValidDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var act = () => _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Connection refused");
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_PublishFails_PropagatesException()
    {
        // Arrange
        var docket = CreateValidDocket();
        var validators = CreateValidatorList(1);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);
        _peerClientMock.Setup(x => x.PublishProposedDocketAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Broadcast timed out"));

        // Act
        var act = () => _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("Broadcast timed out");
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_CancellationRequested_PassesTokenToPeerClient()
    {
        // Arrange
        var docket = CreateValidDocket();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, token))
            .ReturnsAsync(CreateValidatorList(1));

        // Act
        await _distributor.BroadcastProposedDocketAsync(docket, token);

        // Assert
        _peerClientMock.Verify(
            x => x.QueryValidatorsAsync(docket.RegisterId, token),
            Times.Once);
        _peerClientMock.Verify(
            x => x.PublishProposedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                It.IsAny<byte[]>(),
                token),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_CancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        var docket = CreateValidDocket();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var act = () => _distributor.BroadcastProposedDocketAsync(docket, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BroadcastProposedDocketAsync_ManyPeers_ReturnsCorrectCount()
    {
        // Arrange
        var docket = CreateValidDocket();
        var validators = CreateValidatorList(10);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act
        var peerCount = await _distributor.BroadcastProposedDocketAsync(docket);

        // Assert
        peerCount.Should().Be(10);
    }

    #endregion

    #region BroadcastConfirmedDocketAsync Tests

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_ValidDocket_BroadcastsToPeers()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        var validators = CreateValidatorList(2);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act
        var peerCount = await _distributor.BroadcastConfirmedDocketAsync(docket);

        // Assert
        peerCount.Should().Be(2);
        _peerClientMock.Verify(
            x => x.BroadcastConfirmedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _distributor.BroadcastConfirmedDocketAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_NoPeers_ReturnsZero()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidatorInfo>());

        // Act
        var peerCount = await _distributor.BroadcastConfirmedDocketAsync(docket);

        // Assert
        peerCount.Should().Be(0);
    }

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_PeerQueryFails_PropagatesException()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network unreachable"));

        // Act
        var act = () => _distributor.BroadcastConfirmedDocketAsync(docket);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Network unreachable");
    }

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_BroadcastFails_PropagatesException()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        var validators = CreateValidatorList(3);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);
        _peerClientMock.Setup(x => x.BroadcastConfirmedDocketAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Broadcast timed out"));

        // Act
        var act = () => _distributor.BroadcastConfirmedDocketAsync(docket);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task BroadcastConfirmedDocketAsync_CancellationRequested_PassesTokenToPeerClient()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, token))
            .ReturnsAsync(CreateValidatorList(1));

        // Act
        await _distributor.BroadcastConfirmedDocketAsync(docket, token);

        // Assert
        _peerClientMock.Verify(
            x => x.BroadcastConfirmedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                It.IsAny<byte[]>(),
                token),
            Times.Once);
    }

    #endregion

    #region SubmitToRegisterServiceAsync Tests

    [Fact]
    public async Task SubmitToRegisterServiceAsync_ValidDocket_ReturnsTrue()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _distributor.SubmitToRegisterServiceAsync(docket);

        // Assert
        result.Should().BeTrue();
        _registerClientMock.Verify(
            x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_RegisterServiceRejects_ReturnsFalse()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _distributor.SubmitToRegisterServiceAsync(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_Exception_ReturnsFalse()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        var result = await _distributor.SubmitToRegisterServiceAsync(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _distributor.SubmitToRegisterServiceAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_HttpRequestException_ReturnsFalseDoesNotThrow()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Service unavailable"));

        // Act
        var result = await _distributor.SubmitToRegisterServiceAsync(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_TimeoutException_ReturnsFalseDoesNotThrow()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Request timed out"));

        // Act
        var result = await _distributor.SubmitToRegisterServiceAsync(docket);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_CancellationRequested_PassesTokenToRegisterClient()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), token))
            .ReturnsAsync(true);

        // Act
        await _distributor.SubmitToRegisterServiceAsync(docket, token);

        // Assert
        _registerClientMock.Verify(
            x => x.WriteDocketAsync(It.IsAny<DocketModel>(), token),
            Times.Once);
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _distributor.GetStats();

        // Assert
        stats.TotalProposedBroadcasts.Should().Be(0);
        stats.TotalConfirmedBroadcasts.Should().Be(0);
        stats.TotalRegisterSubmissions.Should().Be(0);
        stats.FailedRegisterSubmissions.Should().Be(0);
        stats.AverageBroadcastTimeMs.Should().Be(0);
        stats.LastBroadcastAt.Should().BeNull();
    }

    [Fact]
    public async Task GetStats_AfterBroadcasts_TracksCorrectly()
    {
        // Arrange
        var docket = CreateValidDocket();
        var validators = CreateValidatorList(1);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _distributor.BroadcastProposedDocketAsync(docket);
        await _distributor.BroadcastConfirmedDocketAsync(CreateConfirmedDocket());
        await _distributor.SubmitToRegisterServiceAsync(CreateConfirmedDocket());

        var stats = _distributor.GetStats();

        // Assert
        stats.TotalProposedBroadcasts.Should().Be(1);
        stats.TotalConfirmedBroadcasts.Should().Be(1);
        stats.TotalRegisterSubmissions.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_AfterSuccessfulBroadcast_RecordsBroadcastTime()
    {
        // Arrange
        var docket = CreateValidDocket();
        var validators = CreateValidatorList(1);

        _peerClientMock.Setup(x => x.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act
        await _distributor.BroadcastProposedDocketAsync(docket);
        var stats = _distributor.GetStats();

        // Assert
        stats.AverageBroadcastTimeMs.Should().BeGreaterThanOrEqualTo(0);
        stats.LastBroadcastAt.Should().NotBeNull();
        stats.LastBroadcastAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetStats_AfterRejectedSubmission_TracksFailedCount()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _distributor.SubmitToRegisterServiceAsync(docket);
        var stats = _distributor.GetStats();

        // Assert
        stats.FailedRegisterSubmissions.Should().Be(1);
        stats.TotalRegisterSubmissions.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterExceptionInSubmission_TracksFailedCount()
    {
        // Arrange
        var docket = CreateConfirmedDocket();
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        // Act
        await _distributor.SubmitToRegisterServiceAsync(docket);
        var stats = _distributor.GetStats();

        // Assert
        stats.FailedRegisterSubmissions.Should().Be(1);
    }

    [Fact]
    public async Task GetStats_MultipleBroadcasts_AveragesBroadcastTime()
    {
        // Arrange
        var validators = CreateValidatorList(1);
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act - broadcast multiple times
        for (var i = 0; i < 5; i++)
        {
            await _distributor.BroadcastProposedDocketAsync(CreateValidDocket());
        }

        var stats = _distributor.GetStats();

        // Assert
        stats.TotalProposedBroadcasts.Should().Be(5);
        stats.AverageBroadcastTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetStats_FailedBroadcast_DoesNotIncrementCount()
    {
        // Arrange
        var docket = CreateValidDocket();
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(docket.RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        try
        {
            await _distributor.BroadcastProposedDocketAsync(docket);
        }
        catch (HttpRequestException)
        {
            // Expected
        }

        var stats = _distributor.GetStats();

        // Assert - failed broadcast should not increment proposed count
        stats.TotalProposedBroadcasts.Should().Be(0);
    }

    #endregion

    #region Concurrent Distribution Tests

    [Fact]
    public async Task BroadcastProposedDocketAsync_ConcurrentCalls_TracksAllBroadcasts()
    {
        // Arrange
        var validators = CreateValidatorList(3);
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);

        // Act - fire multiple broadcasts concurrently
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _distributor.BroadcastProposedDocketAsync(CreateValidDocket()))
            .ToArray();

        await Task.WhenAll(tasks);
        var stats = _distributor.GetStats();

        // Assert
        stats.TotalProposedBroadcasts.Should().Be(10);
        stats.AverageBroadcastTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task SubmitToRegisterServiceAsync_ConcurrentCalls_TracksAllSubmissions()
    {
        // Arrange
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _distributor.SubmitToRegisterServiceAsync(CreateConfirmedDocket()))
            .ToArray();

        await Task.WhenAll(tasks);
        var stats = _distributor.GetStats();

        // Assert
        stats.TotalRegisterSubmissions.Should().Be(10);
        stats.FailedRegisterSubmissions.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentMixedOperations_StatsRemainConsistent()
    {
        // Arrange
        var validators = CreateValidatorList(2);
        _peerClientMock.Setup(x => x.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validators);
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - run proposed broadcasts, confirmed broadcasts, and submissions concurrently
        var proposedTasks = Enumerable.Range(0, 5)
            .Select(_ => (Task)_distributor.BroadcastProposedDocketAsync(CreateValidDocket()));
        var confirmedTasks = Enumerable.Range(0, 5)
            .Select(_ => (Task)_distributor.BroadcastConfirmedDocketAsync(CreateConfirmedDocket()));
        var submitTasks = Enumerable.Range(0, 5)
            .Select(_ => (Task)_distributor.SubmitToRegisterServiceAsync(CreateConfirmedDocket()));

        await Task.WhenAll(proposedTasks.Concat(confirmedTasks).Concat(submitTasks));
        var stats = _distributor.GetStats();

        // Assert
        stats.TotalProposedBroadcasts.Should().Be(5);
        stats.TotalConfirmedBroadcasts.Should().Be(5);
        stats.TotalRegisterSubmissions.Should().Be(5);
        stats.FailedRegisterSubmissions.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentSubmissions_MixedSuccessAndFailure_TracksCorrectly()
    {
        // Arrange - alternate success and failure
        var callCount = 0;
        _registerClientMock.Setup(x => x.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var count = Interlocked.Increment(ref callCount);
                return count % 2 == 0; // Even calls succeed, odd calls fail
            });

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _distributor.SubmitToRegisterServiceAsync(CreateConfirmedDocket()))
            .ToArray();

        await Task.WhenAll(tasks);
        var stats = _distributor.GetStats();

        // Assert - total should be 10 (successful + failed)
        var totalTracked = stats.TotalRegisterSubmissions + stats.FailedRegisterSubmissions;
        totalTracked.Should().Be(10);
    }

    #endregion

    #region Helper Methods

    private static Docket CreateValidDocket(
        string? docketId = null,
        string? registerId = null,
        long docketNumber = 1)
    {
        return new Docket
        {
            DocketId = docketId ?? $"docket-{Guid.NewGuid()}",
            RegisterId = registerId ?? "register-1",
            DocketNumber = docketNumber,
            DocketHash = "hash-abc123",
            PreviousHash = docketNumber > 0 ? "prev-hash" : null,
            MerkleRoot = "merkle-root",
            CreatedAt = DateTimeOffset.UtcNow,
            ProposerValidatorId = "validator-1",
            Status = DocketStatus.Proposed,
            ProposerSignature = new Signature
            {
                PublicKey = new byte[] { 1, 2, 3 },
                SignatureValue = new byte[] { 4, 5, 6 },
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            Transactions = new List<Transaction>()
        };
    }

    private static Docket CreateConfirmedDocket()
    {
        var docket = CreateValidDocket();
        return new Docket
        {
            DocketId = docket.DocketId,
            RegisterId = docket.RegisterId,
            DocketNumber = docket.DocketNumber,
            DocketHash = docket.DocketHash,
            PreviousHash = docket.PreviousHash,
            MerkleRoot = docket.MerkleRoot,
            CreatedAt = docket.CreatedAt,
            ProposerValidatorId = docket.ProposerValidatorId,
            Status = DocketStatus.Confirmed,
            ProposerSignature = docket.ProposerSignature,
            Transactions = docket.Transactions,
            Votes = new List<ConsensusVote>
            {
                new()
                {
                    VoteId = "vote-1",
                    DocketId = docket.DocketId,
                    ValidatorId = "val-1",
                    Decision = VoteDecision.Approve,
                    VotedAt = DateTimeOffset.UtcNow,
                    DocketHash = docket.DocketHash,
                    ValidatorSignature = new Signature
                    {
                        PublicKey = new byte[] { 1, 2, 3 },
                        SignatureValue = new byte[] { 4, 5, 6 },
                        Algorithm = "ED25519",
                        SignedAt = DateTimeOffset.UtcNow
                    }
                }
            }
        };
    }

    private static List<ValidatorInfo> CreateValidatorList(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new ValidatorInfo
            {
                ValidatorId = $"val-{i}",
                GrpcEndpoint = $"http://val{i}:5000"
            })
            .ToList();
    }

    #endregion
}
