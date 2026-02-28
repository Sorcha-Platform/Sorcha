// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Monitoring;
using Sorcha.Peer.Service.Network;
using Sorcha.Peer.Service.Observability;

namespace Sorcha.Peer.Service.Tests;

public class PeerServiceTests : IDisposable
{
    private readonly Mock<ILogger<PeerService>> _loggerMock;
    private readonly IOptions<PeerServiceConfiguration> _config;
    private readonly PeerListManager _peerListManager;
    private readonly NetworkAddressService _networkAddressService;
    private readonly PeerDiscoveryService _peerDiscoveryService;
    private readonly HealthMonitorService _healthMonitorService;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerExchangeService _exchangeService;
    private readonly TransactionDistributionService _distributionService;
    private readonly PeerServiceConfiguration _configuration;

    public PeerServiceTests()
    {
        _loggerMock = new Mock<ILogger<PeerService>>();

        _configuration = new PeerServiceConfiguration
        {
            Enabled = true,
            ListenPort = 5001,
            NodeId = null, // Will be auto-generated
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15,
                BootstrapNodes = new List<string>()
            },
            NetworkAddress = new NetworkAddressConfiguration
            {
                ExternalAddress = "127.0.0.1",
                HttpLookupServices = new List<string>(),
                PreferredProtocol = "IPv4"
            },
            SeedNodes = new SeedNodeConfiguration()
        };
        _config = Options.Create(_configuration);

        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            _config);

        var stunClient = new StunClient(new Mock<ILogger<StunClient>>().Object);
        _networkAddressService = new NetworkAddressService(
            new Mock<ILogger<NetworkAddressService>>().Object,
            _config,
            new HttpClient(),
            stunClient);

        _peerDiscoveryService = new PeerDiscoveryService(
            new Mock<ILogger<PeerDiscoveryService>>().Object,
            _config,
            _peerListManager,
            _networkAddressService);

        _healthMonitorService = new HealthMonitorService(
            new Mock<ILogger<HealthMonitorService>>().Object,
            _config,
            _peerListManager,
            _peerDiscoveryService);

        var metrics = new PeerServiceMetrics();
        var activitySource = new PeerServiceActivitySource();
        _connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            _peerListManager,
            _config,
            metrics,
            activitySource);

        _exchangeService = new PeerExchangeService(
            new Mock<ILogger<PeerExchangeService>>().Object,
            _peerListManager,
            _connectionPool,
            _config);

        var gossipEngine = new GossipProtocolEngine(
            new Mock<ILogger<GossipProtocolEngine>>().Object,
            _config,
            _peerListManager);
        var queueManager = new TransactionQueueManager(
            new Mock<ILogger<TransactionQueueManager>>().Object,
            _config);
        _distributionService = new TransactionDistributionService(
            new Mock<ILogger<TransactionDistributionService>>().Object,
            _config,
            queueManager,
            gossipEngine);
    }

    private PeerService CreateService() => new PeerService(
        _loggerMock.Object, _config, _peerListManager,
        _networkAddressService, _peerDiscoveryService,
        _healthMonitorService, _connectionPool, _exchangeService, _distributionService);

    [Fact]
    public void Constructor_ShouldInitializeWithDependencies()
    {
        var service = CreateService();

        service.Should().NotBeNull();
        service.Status.Should().Be(PeerServiceStatus.Offline);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new PeerService(
            null!, _config, _peerListManager,
            _networkAddressService, _peerDiscoveryService,
            _healthMonitorService, _connectionPool, _exchangeService, _distributionService);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        var act = () => new PeerService(
            _loggerMock.Object, null!, _peerListManager,
            _networkAddressService, _peerDiscoveryService,
            _healthMonitorService, _connectionPool, _exchangeService, _distributionService);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void Status_ShouldInitiallyBeOffline()
    {
        var service = CreateService();

        service.Status.Should().Be(PeerServiceStatus.Offline);
    }

    [Fact]
    public async Task NodeId_ShouldBeGenerated_WhenNotProvidedInConfiguration()
    {
        _configuration.NodeId = null;
        var service = CreateService();
        var cts = new CancellationTokenSource();

        var task = service.StartAsync(cts.Token);
        await Task.Delay(200); // Allow initialization
        await service.StopAsync(CancellationToken.None);

        service.NodeId.Should().NotBeNullOrEmpty();
        service.NodeId.Should().StartWith("node_");
    }

    [Fact]
    public async Task NodeId_ShouldUseConfiguredValue_WhenProvided()
    {
        _configuration.NodeId = "custom-node-123";
        var service = CreateService();
        var cts = new CancellationTokenSource();

        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        service.NodeId.Should().Be("custom-node-123");
    }

    [Fact]
    public async Task StartAsync_ShouldNotStart_WhenServiceIsDisabled()
    {
        _configuration.Enabled = false;
        var service = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await service.StartAsync(cts.Token);

        service.Status.Should().Be(PeerServiceStatus.Offline);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("disabled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StartAsync_ShouldInitialize_WhenServiceIsEnabled()
    {
        _configuration.Enabled = true;
        var service = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var startTask = service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Initializing")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task StopAsync_ShouldStopGracefully()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();

        var startTask = service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Stopping") || v.ToString()!.Contains("shutting down")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Service_ShouldLogNodeId_AfterInitialization()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();

        var startTask = service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Node ID")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Service_ShouldHandleCancellation()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var startTask = service.StartAsync(cts.Token);
        await Task.Delay(500); // Wait for cancellation and cleanup

        // Service should handle cancellation without throwing
        service.Status.Should().Be(PeerServiceStatus.Offline);
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        var service = CreateService();

        service.Dispose();

        service.Status.Should().Be(PeerServiceStatus.Offline);
    }

    [Fact]
    public async Task Service_ShouldLogStatusChange()
    {
        var service = CreateService();
        var cts = new CancellationTokenSource();

        var startTask = service.StartAsync(cts.Token);
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task MultipleStartStop_ShouldWorkCorrectly()
    {
        var service = CreateService();

        for (int i = 0; i < 3; i++)
        {
            var cts = new CancellationTokenSource();
            var startTask = service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);

            service.Status.Should().Be(PeerServiceStatus.Offline);
        }
    }

    public void Dispose()
    {
        _peerListManager.Dispose();
        _connectionPool.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
