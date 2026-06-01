// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Observability;

namespace Sorcha.Peer.Service.Tests.Distribution;

public class TransactionDistributionServiceTests : IAsyncDisposable
{
    private readonly TransactionDistributionService _service;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceConfiguration _config;

    public TransactionDistributionServiceTests()
    {
        _config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5
            },
            SeedNodes = new SeedNodeConfiguration(),
            TransactionDistribution = new TransactionDistributionConfiguration()
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

        var relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            _peerListManager,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));

        var gossipEngine = new GossipProtocolEngine(
            new Mock<ILogger<GossipProtocolEngine>>().Object,
            Options.Create(_config),
            _peerListManager);

        var queueManager = new TransactionQueueManager(
            new Mock<ILogger<TransactionQueueManager>>().Object,
            Options.Create(_config));

        _service = new TransactionDistributionService(
            new Mock<ILogger<TransactionDistributionService>>().Object,
            Options.Create(_config),
            queueManager,
            gossipEngine,
            relayCommunication);
    }

    [Fact]
    public void Constructor_NullRelayCommunication_ThrowsArgumentNullException()
    {
        var gossipEngine = new GossipProtocolEngine(
            new Mock<ILogger<GossipProtocolEngine>>().Object,
            Options.Create(_config),
            _peerListManager);

        var queueManager = new TransactionQueueManager(
            new Mock<ILogger<TransactionQueueManager>>().Object,
            Options.Create(_config));

        Action act = () => new TransactionDistributionService(
            new Mock<ILogger<TransactionDistributionService>>().Object,
            Options.Create(_config),
            queueManager,
            gossipEngine,
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DistributeTransactionAsync_NATdPeerTarget_DoesNotThrow()
    {
        // Add a NAT'd peer
        await _peerListManager.AddOrUpdatePeerAsync(new PeerNode
        {
            PeerId = "nat-peer-001",
            Address = "",
            Port = 5000,
            SupportedProtocols = new List<string> { "Grpc" }
        });

        var tx = new TransactionNotification
        {
            TransactionId = new string('a', 64),
            OriginPeerId = "test-node",
            Timestamp = DateTimeOffset.UtcNow,
            DataSize = 100
        };

        // Should not throw even when gossip targets include NAT'd peers
        var act = () => _service.DistributeTransactionAsync(tx);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_NoCarrier_NoFanOut()
    {
        // Feature 145 (T034) — carrier-aware fan-out. No peer carries this register (no channel, no
        // reverse-stream owner) ⇒ no fan-out. Counts only; there is no LocallyOwned signal. The local
        // validator seals if this node is on the roster.
        var service = BuildServiceWithPool();

        var (targets, accepted) = await service.ForwardSubmissionAsync(
            "register-unknown", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0);
        accepted.Should().Be(0);
    }

    [Fact]
    public async Task ForwardSubmissionAsync_SeedConfiguredButNotACarrier_NoFanOut()
    {
        // The core carrier-awareness guarantee: a configured bootstrap seed that is NOT a carrier of
        // this register (never subscribed to it) is NEVER a fan-out target. Previously the seed/topology
        // fallback dialed it regardless — and an unreachable seed hung the submit (the 504). Now: no
        // channel + seed-is-not-a-carrier ⇒ zero targets, no dial.
        await _peerListManager.AddOrUpdatePeerAsync(new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed.example",
            Port = 50051,
            IsSeedNode = true,
            SupportedProtocols = new List<string> { "GrpcStream" }
        });

        var service = BuildServiceWithPool();

        var (targets, accepted) = await service.ForwardSubmissionAsync(
            "register-unknown", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0, "a non-carrier seed must never be fanned out to");
        accepted.Should().Be(0);
    }

    private TransactionDistributionService BuildServiceWithPool()
    {
        var gossipEngine = new GossipProtocolEngine(
            new Mock<ILogger<GossipProtocolEngine>>().Object,
            Options.Create(_config),
            _peerListManager);
        var queueManager = new TransactionQueueManager(
            new Mock<ILogger<TransactionQueueManager>>().Object,
            Options.Create(_config));
        var relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            _peerListManager,
            Options.Create(_config),
            new Lazy<RelayMessageHandler>(() => null!));

        return new TransactionDistributionService(
            new Mock<ILogger<TransactionDistributionService>>().Object,
            Options.Create(_config),
            queueManager,
            gossipEngine,
            relayCommunication,
            _connectionPool,
            _peerListManager);
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
    }
}
