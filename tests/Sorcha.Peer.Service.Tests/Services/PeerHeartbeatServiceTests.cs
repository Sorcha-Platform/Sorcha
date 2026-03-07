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
using Sorcha.Peer.Service.Replication;
using Sorcha.Peer.Service.Services;

namespace Sorcha.Peer.Service.Tests.Services;

public class PeerHeartbeatBackgroundServiceTests : IAsyncDisposable
{
    private readonly PeerHeartbeatBackgroundService _service;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly RegisterAdvertisementService _advertisementService;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;

    public PeerHeartbeatBackgroundServiceTests()
    {
        var config = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "test-node",
            RegisterSync = new RegisterSyncConfiguration
            {
                HeartbeatIntervalSeconds = 30,
                HeartbeatTimeoutSeconds = 10
            },
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
            config);

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
            config,
            _metrics,
            _activitySource);

        _advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager);

        _service = new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _metrics,
            _activitySource,
            config);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            null!,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _metrics,
            _activitySource,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullConnectionPool_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            null!,
            _peerListManager,
            _advertisementService,
            _metrics,
            _activitySource,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPeerListManager_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            _connectionPool,
            null!,
            _advertisementService,
            _metrics,
            _activitySource,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullAdvertisementService_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            _connectionPool,
            _peerListManager,
            null!,
            _metrics,
            _activitySource,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullMetrics_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            null!,
            _activitySource,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullActivitySource_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatBackgroundService(
            new Mock<ILogger<PeerHeartbeatBackgroundService>>().Object,
            _connectionPool,
            _peerListManager,
            _advertisementService,
            _metrics,
            null!,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ValidDependencies_Succeeds()
    {
        _service.Should().NotBeNull();
    }

    [Fact]
    public void GetCurrentSequenceNumber_Initial_ReturnsZero()
    {
        _service.GetCurrentSequenceNumber().Should().Be(0);
    }

    public async ValueTask DisposeAsync()
    {
        _service.Dispose();
        await _connectionPool.DisposeAsync();
        _peerListManager.Dispose();
        _metrics.Dispose();
        _activitySource.Dispose();
    }
}

public class PeerHeartbeatGrpcServiceTests
{
    private readonly PeerHeartbeatGrpcService _grpcService;
    private readonly PeerListManager _peerListManager;
    private readonly RegisterAdvertisementService _advertisementService;

    public PeerHeartbeatGrpcServiceTests()
    {
        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(new PeerServiceConfiguration
            {
                NodeId = "test-node",
                PeerDiscovery = new PeerDiscoveryConfiguration
                {
                    MaxPeersInList = 100,
                    MinHealthyPeers = 5,
                    RefreshIntervalMinutes = 15
                },
                SeedNodes = new SeedNodeConfiguration()
            }));

        _advertisementService = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager);

        _grpcService = new PeerHeartbeatGrpcService(
            new Mock<ILogger<PeerHeartbeatGrpcService>>().Object,
            _peerListManager,
            _advertisementService,
            Options.Create(new PeerServiceConfiguration
            {
                NodeId = "test-node"
            }));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatGrpcService(
            null!,
            _peerListManager,
            _advertisementService,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPeerListManager_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatGrpcService(
            new Mock<ILogger<PeerHeartbeatGrpcService>>().Object,
            null!,
            _advertisementService,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullAdvertisementService_ThrowsArgumentNullException()
    {
        Action act = () => new PeerHeartbeatGrpcService(
            new Mock<ILogger<PeerHeartbeatGrpcService>>().Object,
            _peerListManager,
            null!,
            Options.Create(new PeerServiceConfiguration()));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ValidDependencies_Succeeds()
    {
        _grpcService.Should().NotBeNull();
    }
}
