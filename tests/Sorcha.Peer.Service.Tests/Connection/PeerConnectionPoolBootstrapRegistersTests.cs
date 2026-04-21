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

/// <summary>
/// Without a PeerRouter to introduce peers, a fresh node must announce itself to every
/// seed it dials at startup — otherwise the seed silently drops every subsequent heartbeat
/// advertisement (RegisterAdvertisementService.ProcessRemoteAdvertisementsAsync requires
/// the source peer to already be known). These tests pin the bootstrap-time RegisterPeer
/// invocation so the regression doesn't sneak back in.
/// </summary>
public class PeerConnectionPoolBootstrapRegistersTests : IAsyncDisposable
{
    private readonly RecordingPeerConnectionPool _pool;
    private readonly PeerListManager _peerListManager;

    public PeerConnectionPoolBootstrapRegistersTests()
    {
        var peerLoggerMock = new Mock<ILogger<PeerListManager>>();
        var listConfig = Options.Create(new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 50,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            }
        });
        _peerListManager = new PeerListManager(peerLoggerMock.Object, listConfig);

        var poolConfig = Options.Create(new PeerServiceConfiguration
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
                    new SeedNodeEndpoint { NodeId = "seed-alpha", Hostname = "alpha.local", Port = 5000 },
                    new SeedNodeEndpoint { NodeId = "seed-beta",  Hostname = "beta.local",  Port = 5000 }
                ]
            }
        });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        _pool = new RecordingPeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            poolConfig,
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());
    }

    [Fact]
    public async Task BootstrapFromSeedNodesAsync_RegistersWithEverySuccessfulSeed()
    {
        await _pool.BootstrapFromSeedNodesAsync();

        _pool.RegistrationTargets.Should().BeEquivalentTo(
            new[] { "seed-alpha", "seed-beta" },
            "every seed we successfully dial must be told who we are so it accepts our heartbeats");
    }

    [Fact]
    public async Task BootstrapFromSeedNodesAsync_DoesNotRegister_WhenSeedListIsEmpty()
    {
        var emptyConfig = Options.Create(new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 50,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            SeedNodes = new SeedNodeConfiguration { SeedNodes = [] }
        });
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        await using var emptyPool = new RecordingPeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            _peerListManager,
            emptyConfig,
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        await emptyPool.BootstrapFromSeedNodesAsync();

        emptyPool.RegistrationTargets.Should().BeEmpty();
    }

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private sealed class RecordingPeerConnectionPool : PeerConnectionPool
    {
        public List<string> RegistrationTargets { get; } = [];

        public RecordingPeerConnectionPool(
            ILogger<PeerConnectionPool> logger,
            ILoggerFactory loggerFactory,
            PeerListManager peerListManager,
            IOptions<PeerServiceConfiguration> configuration,
            PeerServiceMetrics metrics,
            PeerServiceActivitySource activitySource)
            : base(logger, loggerFactory, peerListManager, configuration, metrics, activitySource)
        {
        }

        protected internal override Task RegisterWithRemotePeerAsync(string peerId, CancellationToken cancellationToken)
        {
            RegistrationTargets.Add(peerId);
            return Task.CompletedTask;
        }
    }
}
