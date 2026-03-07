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

namespace Sorcha.Peer.Service.Tests.Connection;

public class PeerConnectionPoolTests : IAsyncDisposable
{
    private readonly PeerConnectionPool _pool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;

    public PeerConnectionPoolTests()
    {
        var peerLoggerMock = new Mock<ILogger<PeerListManager>>();
        var peerConfig = Options.Create(new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 50,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            }
        });
        _peerListManager = new PeerListManager(peerLoggerMock.Object, peerConfig);

        var config = Options.Create(new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 50,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration
            {
                SeedNodes =
                [
                    new SeedNodeEndpoint
                    {
                        NodeId = "seed-1",
                        Hostname = "seed1.local",
                        Port = 5000
                    },
                    new SeedNodeEndpoint
                    {
                        NodeId = "seed-2",
                        Hostname = "seed2.local",
                        Port = 5000
                    }
                ]
            }
        });

        _metrics = new PeerServiceMetrics();
        _activitySource = new PeerServiceActivitySource();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _pool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            config,
            _metrics,
            _activitySource);
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDependencies()
    {
        _pool.Should().NotBeNull();
        _pool.ActiveConnectionCount.Should().Be(0);
    }

    [Fact]
    public void MaxConnections_ShouldReflectConfiguration()
    {
        _pool.MaxConnections.Should().Be(50);
    }

    [Fact]
    public async Task ConnectToPeerAsync_ShouldCreateConnection()
    {
        // Act
        var result = await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Assert
        result.Should().BeTrue();
        _pool.ActiveConnectionCount.Should().Be(1);
    }

    [Fact]
    public async Task ConnectToPeerAsync_ShouldReturnFalse_WhenPeerIdIsEmpty()
    {
        // Act
        var result = await _pool.ConnectToPeerAsync("", "http://localhost:5001");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectToPeerAsync_ShouldReturnTrue_WhenAlreadyConnected()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Act
        var result = await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Assert
        result.Should().BeTrue();
        _pool.ActiveConnectionCount.Should().Be(1); // No duplicate
    }

    [Fact]
    public async Task GetChannel_ShouldReturnChannel_WhenConnected()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Act
        var channel = _pool.GetChannel("peer-1");

        // Assert
        channel.Should().NotBeNull();
    }

    [Fact]
    public void GetChannel_ShouldReturnNull_WhenNotConnected()
    {
        // Act
        var channel = _pool.GetChannel("nonexistent");

        // Assert
        channel.Should().BeNull();
    }

    [Fact]
    public async Task GetAllActiveChannels_ShouldReturnAllConnected()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        await _pool.ConnectToPeerAsync("peer-2", "http://localhost:5002");

        // Act
        var channels = _pool.GetAllActiveChannels();

        // Assert
        channels.Should().HaveCount(2);
        channels.Select(c => c.PeerId).Should().Contain(["peer-1", "peer-2"]);
    }

    [Fact]
    public async Task DisconnectPeerAsync_ShouldRemoveConnection()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        _pool.ActiveConnectionCount.Should().Be(1);

        // Act
        await _pool.DisconnectPeerAsync("peer-1");

        // Assert
        _pool.ActiveConnectionCount.Should().Be(0);
        _pool.GetChannel("peer-1").Should().BeNull();
    }

    [Fact]
    public async Task DisconnectPeerAsync_ShouldHandleNonexistentPeer()
    {
        // Act — should not throw
        await _pool.DisconnectPeerAsync("nonexistent");
    }

    [Fact]
    public async Task RecordSuccess_ShouldResetFailureCount()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        await _pool.RecordFailureAsync("peer-1");

        // Act
        _pool.RecordSuccess("peer-1");

        // Assert — peer should still be connected
        _pool.GetChannel("peer-1").Should().NotBeNull();
    }

    [Fact]
    public async Task RecordFailureAsync_ShouldNotRemoveSeedNode()
    {
        // Arrange — add peer as seed node in peer list
        var seedPeer = new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed1.local",
            Port = 5000,
            IsSeedNode = true
        };
        await _peerListManager.AddOrUpdatePeerAsync(seedPeer);
        await _pool.ConnectToPeerAsync("seed-1", "http://seed1.local:5000");

        // Act — 5 consecutive failures
        for (int i = 0; i < 5; i++)
        {
            await _pool.RecordFailureAsync("seed-1");
        }

        // Assert — seed node still in pool (marked disconnected, not removed)
        var statuses = _pool.GetConnectionStatuses();
        statuses.Should().ContainKey("seed-1");
    }

    [Fact]
    public async Task GetChannelsForRegister_ShouldReturnOnlyMatchingPeers()
    {
        // Arrange
        var peer1 = new PeerNode
        {
            PeerId = "peer-1",
            Address = "192.168.1.100",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo { RegisterId = "reg-1" }
            ]
        };
        var peer2 = new PeerNode
        {
            PeerId = "peer-2",
            Address = "192.168.1.101",
            Port = 5001,
            AdvertisedRegisters =
            [
                new PeerRegisterInfo { RegisterId = "reg-2" }
            ]
        };

        await _peerListManager.AddOrUpdatePeerAsync(peer1);
        await _peerListManager.AddOrUpdatePeerAsync(peer2);
        await _pool.ConnectToPeerAsync("peer-1", "http://192.168.1.100:5001");
        await _pool.ConnectToPeerAsync("peer-2", "http://192.168.1.101:5001");

        // Act
        var channels = _pool.GetChannelsForRegister("reg-1");

        // Assert
        channels.Should().ContainSingle().Which.PeerId.Should().Be("peer-1");
    }

    [Fact]
    public async Task CleanupIdleConnectionsAsync_ShouldRemoveIdleNonSeedConnections()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        await _pool.ConnectToPeerAsync("peer-2", "http://localhost:5002");

        // Act — cleanup with very short idle timeout (everything is "idle")
        await _pool.CleanupIdleConnectionsAsync(TimeSpan.Zero);

        // Assert
        _pool.ActiveConnectionCount.Should().Be(0);
    }

    [Fact]
    public async Task CleanupIdleConnectionsAsync_ShouldNotRemoveSeedNodes()
    {
        // Arrange
        var seedPeer = new PeerNode
        {
            PeerId = "seed-1",
            Address = "seed1.local",
            Port = 5000,
            IsSeedNode = true
        };
        await _peerListManager.AddOrUpdatePeerAsync(seedPeer);
        await _pool.ConnectToPeerAsync("seed-1", "http://seed1.local:5000");
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");

        // Act — cleanup with zero idle time
        await _pool.CleanupIdleConnectionsAsync(TimeSpan.Zero);

        // Assert
        var statuses = _pool.GetConnectionStatuses();
        statuses.Should().ContainKey("seed-1"); // Seed kept
        statuses.Should().NotContainKey("peer-1"); // Regular peer cleaned up
    }

    [Fact]
    public async Task GetConnectionStatuses_ShouldReturnAllStatuses()
    {
        // Arrange
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        await _pool.ConnectToPeerAsync("peer-2", "http://localhost:5002");

        // Act
        var statuses = _pool.GetConnectionStatuses();

        // Assert
        statuses.Should().HaveCount(2);
        statuses["peer-1"].Should().BeTrue();
        statuses["peer-2"].Should().BeTrue();
    }

    [Fact]
    public async Task MultipleConnections_ShouldMaintainSeparateChannels()
    {
        // Act
        await _pool.ConnectToPeerAsync("peer-1", "http://localhost:5001");
        await _pool.ConnectToPeerAsync("peer-2", "http://localhost:5002");
        await _pool.ConnectToPeerAsync("peer-3", "http://localhost:5003");

        // Assert
        _pool.ActiveConnectionCount.Should().Be(3);
        _pool.GetChannel("peer-1").Should().NotBeNull();
        _pool.GetChannel("peer-2").Should().NotBeNull();
        _pool.GetChannel("peer-3").Should().NotBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}
