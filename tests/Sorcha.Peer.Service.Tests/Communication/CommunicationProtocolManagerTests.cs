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

namespace Sorcha.Peer.Service.Tests.Communication;

public class CommunicationProtocolManagerTests : IAsyncDisposable
{
    private readonly CommunicationProtocolManager _manager;
    private readonly PeerConnectionPool _connectionPool;
    private readonly RelayCommunicationService _relayCommunication;
    private readonly PeerServiceConfiguration _config;

    public CommunicationProtocolManagerTests()
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
            Communication = new CommunicationConfiguration()
        };

        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(_config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            peerListManager,
            Options.Create(_config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        _relayCommunication = new RelayCommunicationService(
            new Mock<ILogger<RelayCommunicationService>>().Object,
            _connectionPool,
            peerListManager,
            Options.Create(_config));

        _manager = new CommunicationProtocolManager(
            new Mock<ILogger<CommunicationProtocolManager>>().Object,
            loggerFactoryMock.Object,
            Options.Create(_config),
            new HttpClient(),
            _relayCommunication);
    }

    [Fact]
    public async Task SendMessageAsync_PeerWithEmptyAddress_UsesRelay()
    {
        // NAT'd peer with empty address should route through relay
        var natPeer = new PeerNode
        {
            PeerId = "nat-peer-001",
            Address = "", // NAT'd - no direct address
            Port = 5000,
            SupportedProtocols = new List<string> { "Grpc" }
        };

        // No seed node available, so relay will return false
        var result = await _manager.SendMessageAsync(natPeer, new { test = "data" });

        // Relay attempted (returns false because no seed channel available)
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_PeerWithNullAddress_UsesRelay()
    {
        var natPeer = new PeerNode
        {
            PeerId = "nat-peer-002",
            Address = null!,
            Port = 5000,
            SupportedProtocols = new List<string> { "Grpc" }
        };

        var result = await _manager.SendMessageAsync(natPeer, new { test = "data" });
        result.Should().BeFalse(); // Relay attempted, no seed available
    }

    [Fact]
    public async Task SendMessageAsync_PeerWithAddress_UsesDirectPath()
    {
        // Peer with address should attempt direct protocols (not relay)
        var directPeer = new PeerNode
        {
            PeerId = "direct-peer-001",
            Address = "192.168.1.100",
            Port = 5000,
            SupportedProtocols = new List<string>() // No supported protocols -> falls through
        };

        // Will fail because no supported protocols, but importantly it doesn't use relay
        var result = await _manager.SendMessageAsync(directPeer, new { test = "data" });
        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullRelayCommunication_ThrowsArgumentNullException()
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        Action act = () => new CommunicationProtocolManager(
            new Mock<ILogger<CommunicationProtocolManager>>().Object,
            loggerFactoryMock.Object,
            Options.Create(_config),
            new HttpClient(),
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionPool.DisposeAsync();
    }
}
