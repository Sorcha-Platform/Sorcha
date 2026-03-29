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
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Tests.Replication;

public class RegisterReplicationServiceTests : IAsyncDisposable
{
    private readonly RegisterReplicationService _service;
    private readonly RegisterReplicationService _serviceWithRelay;
    private readonly PeerListManager _peerListManager;
    private readonly PeerConnectionPool _connectionPool;
    private readonly RegisterCache _registerCache;
    private readonly RelayCommunicationService _relayCommunication;
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

        _service = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
            _registerCache);

        _serviceWithRelay = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            _connectionPool,
            _peerListManager,
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

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
