// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;

namespace Sorcha.Peer.Service.Tests.Discovery;

public class PeerExchangeServiceTests : IAsyncDisposable
{
    private readonly PeerExchangeService _exchangeService;
    private readonly PeerListManager _peerListManager;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;

    public PeerExchangeServiceTests()
    {
        var peerConfig = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "test-node",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration()
        });

        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            peerConfig);

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
            peerConfig,
            _metrics,
            _activitySource);

        _exchangeService = new PeerExchangeService(
            new Mock<ILogger<PeerExchangeService>>().Object,
            _peerListManager,
            _connectionPool,
            peerConfig);
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDependencies()
    {
        _exchangeService.Should().NotBeNull();
    }

    [Fact]
    public async Task ExchangeWithPeersAsync_ShouldReturnZero_WhenNoActiveConnections()
    {
        // Act
        var result = await _exchangeService.ExchangeWithPeersAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task FindPeersForRegisterAsync_ShouldReturnLocalPeers_WhenAvailable()
    {
        // Arrange — add a peer with register info
        var peer = new PeerNode
        {
            PeerId = "peer-with-reg",
            Address = "192.168.1.100",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo { RegisterId = "reg-1", SyncState = RegisterSyncState.Active }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        // Act
        var result = await _exchangeService.FindPeersForRegisterAsync("reg-1");

        // Assert
        result.Should().ContainSingle().Which.PeerId.Should().Be("peer-with-reg");
    }

    [Fact]
    public async Task FindPeersForRegisterAsync_ShouldReturnEmpty_WhenNoLocalOrRemotePeers()
    {
        // Act
        var result = await _exchangeService.FindPeersForRegisterAsync("nonexistent-reg");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindPeersForRegisterAsync_RequireFullReplica_FiltersCorrectly()
    {
        // Arrange
        var activePeer = new PeerNode
        {
            PeerId = "active-peer",
            Address = "192.168.1.100",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo { RegisterId = "reg-1", SyncState = RegisterSyncState.Active }
            ]
        };
        var replicaPeer = new PeerNode
        {
            PeerId = "replica-peer",
            Address = "192.168.1.101",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo { RegisterId = "reg-1", SyncState = RegisterSyncState.FullyReplicated }
            ]
        };
        await _peerListManager.AddOrUpdatePeerAsync(activePeer);
        await _peerListManager.AddOrUpdatePeerAsync(replicaPeer);

        // Act
        var result = await _exchangeService.FindPeersForRegisterAsync("reg-1", requireFullReplica: true);

        // Assert
        result.Should().ContainSingle().Which.PeerId.Should().Be("replica-peer");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        Action act = () => new PeerExchangeService(
            null!,
            _peerListManager,
            _connectionPool,
            Options.Create(new PeerServiceConfiguration
            {
                PeerDiscovery = new PeerDiscoveryConfiguration()
            }));

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
