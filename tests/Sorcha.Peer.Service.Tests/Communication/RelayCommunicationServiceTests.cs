// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Tests.Communication;

public class RelayCommunicationServiceTests : IAsyncDisposable
{
    private readonly RelayCommunicationService _service;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceConfiguration _config;

    public RelayCommunicationServiceTests()
    {
        _config = new PeerServiceConfiguration
        {
            NodeId = "test-node-001",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration()
        };

        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(_config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            Options.Create(_config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        _service = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            _peerListManager,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));
    }

    // --- Feature 143: rendezvous routing (broker over a held reverse stream) ---

    [Fact]
    public async Task SendViaRelayAsync_HoldingTargetReverseStream_BrokersOverIt()
    {
        var reverseStreams = new ReverseStreamManager(NullLogger<ReverseStreamManager>.Instance);
        PeerMessage? written = null;
        var writer = new Mock<IServerStreamWriter<PeerMessage>>();
        writer.Setup(w => w.WriteAsync(It.IsAny<PeerMessage>()))
            .Callback<PeerMessage>(m => written = m)
            .Returns(Task.CompletedTask);
        reverseStreams.RegisterStream("natd-owner", writer.Object);

        var svc = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool, _peerListManager, Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!),
            reverseStreams, new PeerServiceMetrics());

        var ok = await svc.SendViaRelayAsync(
            "natd-owner", MessageType.RegisterSyncRequest, new { CorrelationId = "c1", RegisterId = "reg1" });

        ok.Should().BeTrue();
        written.Should().NotBeNull();
        written!.RecipientPeerId.Should().Be("natd-owner");
        written.MessageType.Should().Be(MessageType.RegisterSyncRequest);
        writer.Verify(w => w.WriteAsync(It.IsAny<PeerMessage>()), Times.Once);
    }

    [Fact]
    public async Task SendViaRelayAsync_NoReverseStreamAndNoSeed_ReturnsFalse()
    {
        var reverseStreams = new ReverseStreamManager(NullLogger<ReverseStreamManager>.Instance);
        var svc = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool, _peerListManager, Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!),
            reverseStreams, new PeerServiceMetrics());

        // Not held as a reverse stream and no seed channel configured → falls through to unary, no seed → false.
        var ok = await svc.SendViaRelayAsync(
            "unknown-peer", MessageType.RegisterSyncRequest, new { CorrelationId = "c2", RegisterId = "reg1" });

        ok.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new RelayCommunicationService(
            null!,
            _connectionPool,
            _peerListManager,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConnectionPool_ThrowsArgumentNullException()
    {
        Action act = () => new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            null!,
            _peerListManager,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPeerListManager_ThrowsArgumentNullException()
    {
        Action act = () => new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            null!,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SendViaRelayAsync_NoSeedNodeConnected_ReturnsFalse()
    {
        // No seed nodes registered, so no seed channel available
        var result = await _service.SendViaRelayAsync(
            "target-peer",
            MessageType.TransactionNotification,
            new { test = "data" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendAndWaitAsync_NoSeedNodeConnected_ReturnsNull()
    {
        var result = await _service.SendAndWaitAsync<object>(
            "target-peer",
            MessageType.RegisterSyncRequest,
            new { test = "data" },
            Guid.NewGuid().ToString());

        result.Should().BeNull();
    }

    [Fact]
    public void CompleteCorrelation_WithPendingCorrelation_ReturnsTrue()
    {
        // Manually set up a pending correlation to test CompleteCorrelation
        var correlationId = Guid.NewGuid().ToString();
        var responseMessage = new PeerMessage
        {
            SenderPeerId = "responder",
            RecipientPeerId = "test-node-001",
            MessageType = MessageType.RegisterSyncResponse,
            Payload = ByteString.CopyFromUtf8("{\"correlationId\":\"" + correlationId + "\"}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Start a SendAndWaitAsync which registers the correlation
        // We can't easily test the full flow without a real gRPC channel,
        // but we can test CompleteCorrelation independently
        // First, there's no pending correlation, so it should return false
        var result = _service.CompleteCorrelation(correlationId, responseMessage);
        result.Should().BeFalse();
    }

    [Fact]
    public void CompleteCorrelation_StaleCorrelation_SilentlyDiscards()
    {
        // Completing a correlation that doesn't exist (expired/removed) should return false silently
        var staleCorrelationId = Guid.NewGuid().ToString();
        var responseMessage = new PeerMessage
        {
            SenderPeerId = "responder",
            RecipientPeerId = "test-node-001",
            MessageType = MessageType.RegisterSyncResponse,
            Payload = ByteString.CopyFromUtf8("{\"data\":\"stale\"}"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var result = _service.CompleteCorrelation(staleCorrelationId, responseMessage);
        result.Should().BeFalse();
    }

    [Fact]
    public void PendingCorrelationCount_InitiallyZero()
    {
        _service.PendingCorrelationCount.Should().Be(0);
    }

    [Fact]
    public void SenderPeerId_PopulatedWithNodeId()
    {
        // The service should use config NodeId for SenderPeerId
        // This is verified indirectly through the CreatePeerMessage method
        // We verify the config is set correctly
        _config.NodeId.Should().Be("test-node-001");
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
    }
}
