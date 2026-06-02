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
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Peer.Service.Tests.Replication;

public class RegisterReplicationServiceTests : IAsyncDisposable
{
    private readonly RegisterReplicationService _service;
    private readonly RegisterReplicationService _serviceWithRelay;
    private readonly PeerListManager _peerListManager;
    private readonly PeerConnectionPool _connectionPool;
    private readonly RegisterCache _registerCache;
    private readonly RelayCommunicationService _relayCommunication;
    private readonly RegisterAdvertisementService _advertisementService;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;
    private readonly IOptions<PeerServiceConfiguration> _config;

    public RegisterReplicationServiceTests()
    {
        _config = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration(),
            RegisterSync = new RegisterSyncConfiguration()
        });

        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            _config);

        _metrics = new PeerServiceMetrics();
        _activitySource = new PeerServiceActivitySource();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            _config,
            _metrics,
            _activitySource);

        _registerCache = new RegisterCache(
            new Mock<ILogger<RegisterCache>>().Object);

        _relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _config,
            new Lazy<RelayMessageHandler>(() => null!));

        _advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager);

        _service = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _registerCache);

        _serviceWithRelay = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _registerCache,
            _config,
            _relayCommunication);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterReplicationService(
            null!,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _registerCache);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConnectionPool_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            null!,
            _peerListManager,
            _advertisementService,
            _registerCache);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPeerListManager_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            null!,
            _advertisementService,
            _registerCache);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullRegisterCache_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PullFullReplicaAsync_NoPeersAvailable_ReturnsFailure()
    {
        var subscription = new RegisterSubscription
        {
            RegisterId = "orphan-register",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        var result = await _service.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No source peers");
    }

    [Fact]
    public async Task PullFullReplicaAsync_PeersExistButNoChannel_ReturnsAllPeersFailed()
    {
        // Add a peer that advertises the register but has no active connection
        var peer = new PeerNode
        {
            PeerId = "peer-1",
            Address = "192.168.1.100",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-1",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 100
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-1",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        var result = await _service.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("All source peers failed");
    }

    [Fact]
    public async Task PullFullReplicaAsync_NoPeers_DoesNotRecordFailureOnSubscription()
    {
        // When no peers are available at all, the method returns early
        // without calling RecordSyncFailure (caller handles the result)
        var subscription = new RegisterSubscription
        {
            RegisterId = "orphan-register",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing,
            ConsecutiveFailures = 0
        };

        var result = await _service.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        subscription.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task PullFullReplicaAsync_AllPeersFail_RecordsFailure()
    {
        // Add peer with register but no connection — will fail during sync
        var peer = new PeerNode
        {
            PeerId = "peer-1",
            Address = "192.168.1.100",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-1",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 100
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-1",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing,
            ConsecutiveFailures = 0
        };

        await _service.PullFullReplicaAsync(subscription);

        subscription.ConsecutiveFailures.Should().Be(1);
        subscription.ErrorMessage.Should().Contain("All source peers failed");
    }

    [Fact]
    public async Task PullFullReplicaAsync_NatdPeerWithRelay_AttemptsRelaySync()
    {
        // NAT'd peer: empty address, no channel
        var peer = new PeerNode
        {
            PeerId = "nat-peer-1",
            Address = "",
            Port = 5000,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-relay",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 50
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-relay",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        // Will attempt relay sync but fail (no seed channel) — should not throw
        var result = await _serviceWithRelay.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("All source peers failed");
    }

    [Fact]
    public async Task PullFullReplicaAsync_PeerWithAddressButNoChannel_SkipsRelayPath()
    {
        // Peer has address (not NAT'd) but no channel — should skip, not relay
        var peer = new PeerNode
        {
            PeerId = "direct-peer-1",
            Address = "192.168.1.200",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-direct",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 100
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-direct",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        // Peer has address so relay path should be skipped — falls through to "All source peers failed"
        var result = await _serviceWithRelay.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("All source peers failed");
    }

    [Fact]
    public async Task PullFullReplicaAsync_NatdPeerWithoutRelay_SkipsPeer()
    {
        // NAT'd peer but service has no relay dependency — should skip peer
        var peer = new PeerNode
        {
            PeerId = "nat-peer-2",
            Address = "",
            Port = 5000,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-no-relay",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 50
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-no-relay",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing
        };

        // Service without relay should skip NAT'd peer (no channel, no relay)
        var result = await _service.PullFullReplicaAsync(subscription);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("All source peers failed");
    }

    [Fact]
    public async Task PullFullReplicaAsync_NatdPeerRelaySyncFails_RecordsFailure()
    {
        var peer = new PeerNode
        {
            PeerId = "nat-peer-fail",
            Address = "",
            Port = 5000,
            FailureCount = 0,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo
                {
                    RegisterId = "reg-fail",
                    SyncState = RegisterSyncState.FullyReplicated,
                    LatestVersion = 10
                }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var subscription = new RegisterSubscription
        {
            RegisterId = "reg-fail",
            Mode = ReplicationMode.FullReplica,
            SyncState = RegisterSyncState.Syncing,
            ConsecutiveFailures = 0
        };

        var result = await _serviceWithRelay.PullFullReplicaAsync(subscription);

        // Relay sync failed (no seed) — subscription failure recorded
        result.Success.Should().BeFalse();
        subscription.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task SubscribeToLiveTransactionsAsync_NoPeers_ReturnsWithoutError()
    {
        var subscription = new RegisterSubscription
        {
            RegisterId = "orphan-register",
            Mode = ReplicationMode.ForwardOnly,
            SyncState = RegisterSyncState.Active
        };

        // Should complete without throwing
        await _service.SubscribeToLiveTransactionsAsync(subscription);
    }

    // ---- Issue #908: incremental sync floor reconciled against the actual local register height ----

    /// <summary>
    /// Builds a service whose co-located Register Service reports <paramref name="localHeight"/> as
    /// the local docket count for any register (a real DI scope chain, so CreateAsyncScope works).
    /// </summary>
    private RegisterReplicationService ServiceWithLocalHeight(long localHeight)
    {
        var registerClient = new Mock<IRegisterServiceClient>();
        registerClient
            .Setup(c => c.GetRegisterHeightAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(localHeight);

        var services = new ServiceCollection();
        services.AddScoped(_ => registerClient.Object);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _registerCache,
            _config,
            _relayCommunication,
            docketFinalizationService: null,
            scopeFactory: scopeFactory);
    }

    [Fact]
    public async Task ResolveFromVersion_EmptyLocalRegister_RequestsFromMinusOne()
    {
        // height=-1 (register absent locally) ⇒ request from -1 so the owner serves from docket 0.
        var svc = ServiceWithLocalHeight(-1);
        var subscription = new RegisterSubscription { RegisterId = "reg-1", LastSyncedDocketVersion = 1 };

        var fromVersion = await svc.ResolveFromVersionAsync(subscription, CancellationToken.None);

        fromVersion.Should().Be(-1,
            "an empty subscription must request from -1 even when a stale cursor says otherwise (the #908 bug)");
    }

    [Fact]
    public async Task ResolveFromVersion_LocalHeightZero_RequestsFromMinusOne()
    {
        // A 0-count register holds nothing ⇒ still request from -1 (full backfill from genesis).
        var svc = ServiceWithLocalHeight(0);
        var subscription = new RegisterSubscription { RegisterId = "reg-1", LastSyncedDocketVersion = 1 };

        var fromVersion = await svc.ResolveFromVersionAsync(subscription, CancellationToken.None);

        fromVersion.Should().Be(-1);
    }

    [Theory]
    [InlineData(1, 0)]  // holds docket 0 ⇒ request the tail after index 0
    [InlineData(2, 1)]  // holds dockets 0,1 ⇒ request after index 1
    [InlineData(5, 4)]  // holds 0..4 ⇒ request after index 4
    public async Task ResolveFromVersion_NonEmptyLocalRegister_RequestsIncrementalTail(long localHeight, long expectedFromVersion)
    {
        var svc = ServiceWithLocalHeight(localHeight);
        var subscription = new RegisterSubscription { RegisterId = "reg-1", LastSyncedDocketVersion = 0 };

        var fromVersion = await svc.ResolveFromVersionAsync(subscription, CancellationToken.None);

        fromVersion.Should().Be(expectedFromVersion,
            "the floor is the highest docket index already held (height-1), so the owner serves only the missing tail");
    }

    [Fact]
    public async Task ResolveFromVersion_NoScopeFactory_FallsBackToSubscriptionCursor()
    {
        // Unit-test path (and any context without a co-located Register Service): preserve the prior
        // behaviour of trusting the persisted cursor rather than failing.
        var subscription = new RegisterSubscription { RegisterId = "reg-1", LastSyncedDocketVersion = 7 };

        var fromVersion = await _service.ResolveFromVersionAsync(subscription, CancellationToken.None);

        fromVersion.Should().Be(7);
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
