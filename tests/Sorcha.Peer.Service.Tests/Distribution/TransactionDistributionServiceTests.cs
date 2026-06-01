// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Observability;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.ServiceClients.Register;

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
    public async Task ForwardSubmissionAsync_NoChannelsAndNoSeeds_ReportsLocallyOwned()
    {
        // No seed nodes configured -> this node owns/standalones the register; an empty
        // channel set legitimately means "no fan-out required".
        var service = BuildServiceWithPool();

        var (targets, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-unknown", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0);
        accepted.Should().Be(0);
        locallyOwned.Should().BeTrue();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_NoChannelsButSeedConfigured_DoesNotClaimLocalOwnership()
    {
        // Cold-start fan-out regression guard: a subscriber (seed node configured) whose
        // register channels are not yet warm must NOT report LocallyOwned=true — otherwise the
        // caller treats the register as locally owned and never fans the transaction out, and it
        // strands in the local unverified pool. Ownership follows configuration (seed presence),
        // not the runtime channel set.
        await _peerListManager.AddOrUpdatePeerAsync(new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed.example",
            Port = 50051,
            IsSeedNode = true,
            SupportedProtocols = new List<string> { "GrpcStream" }
        });

        var service = BuildServiceWithPool();

        var (_, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-unknown", System.Text.Encoding.UTF8.GetBytes("{}"));

        accepted.Should().Be(0);
        locallyOwned.Should().BeFalse();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_RosterOwner_ReportsLocallyOwnedWithoutFanOut()
    {
        // Feature 145 (T017): roster-based sealer selection. When the register's control-record roster
        // marks this node as Owner, its co-located validator seals locally — no fan-out, regardless of
        // channel/seed state. Configure a seed (which the topology heuristic would otherwise fan out to)
        // to prove the roster short-circuits ahead of it.
        await _peerListManager.AddOrUpdatePeerAsync(new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed.example",
            Port = 50051,
            IsSeedNode = true,
            SupportedProtocols = new List<string> { "GrpcStream" }
        });

        var service = BuildServiceWithRoster(RegisterRoleSet.Owner);

        var (targets, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-owned", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0);
        accepted.Should().Be(0);
        locallyOwned.Should().BeTrue();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_RosterValidator_ReportsLocallyOwnedWithoutFanOut()
    {
        // A node on the roster as a Validator also seals locally — same short-circuit as Owner.
        var service = BuildServiceWithRoster(RegisterRoleSet.Validator);

        var (targets, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-validated", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0);
        accepted.Should().Be(0);
        locallyOwned.Should().BeTrue();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_RosterSubscriber_DoesNotShortCircuitAndFansOutToSeed()
    {
        // A subscriber (roster role None) must NOT claim local ownership: it falls through to the
        // transport path and forwards to its configured seed (which fails to connect in this unit
        // context, so accepted=0 but locallyOwned=false — the cold-start fan-out guard).
        await _peerListManager.AddOrUpdatePeerAsync(new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed.example",
            Port = 50051,
            IsSeedNode = true,
            SupportedProtocols = new List<string> { "GrpcStream" }
        });

        var service = BuildServiceWithRoster(RegisterRoleSet.None);

        var (_, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-subscribed", System.Text.Encoding.UTF8.GetBytes("{}"));

        accepted.Should().Be(0);
        locallyOwned.Should().BeFalse();
    }

    [Fact]
    public async Task ForwardSubmissionAsync_RelationshipUnknown_FallsBackToTopologyHeuristic()
    {
        // When the relationship lookup returns null (register not held locally / lookup failed), the
        // service falls back to the seeds/topology heuristic unchanged: no seeds configured ⇒ standalone
        // owner ⇒ locally owned.
        var service = BuildServiceWithRoster(relationship: null);

        var (targets, accepted, locallyOwned) = await service.ForwardSubmissionAsync(
            "register-unknown", System.Text.Encoding.UTF8.GetBytes("{}"));

        targets.Should().Be(0);
        accepted.Should().Be(0);
        locallyOwned.Should().BeTrue();
    }

    private TransactionDistributionService BuildServiceWithRoster(RegisterRoleSet roles)
        => BuildServiceWithRoster(new RegisterLocalRelationship(
            RegisterId: "register",
            Roles: roles,
            ControlRecordVersion: 0,
            DerivedAt: DateTimeOffset.UtcNow));

    private TransactionDistributionService BuildServiceWithRoster(RegisterLocalRelationship? relationship)
    {
        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient
            .Setup(c => c.GetLocalRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(relationship);

        var clientServices = new ServiceCollection();
        clientServices.AddScoped(_ => registerClient.Object);
        var scopeFactory = clientServices.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

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
            _peerListManager,
            reverseStreams: null,
            scopeFactory: scopeFactory);
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
