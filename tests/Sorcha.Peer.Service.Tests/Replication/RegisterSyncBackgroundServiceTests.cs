// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Tests.Replication;

public class RegisterSyncBackgroundServiceTests : IDisposable
{
    private readonly RegisterSyncBackgroundService _service;
    private readonly PeerServiceConfiguration _config;

    public RegisterSyncBackgroundServiceTests()
    {
        _config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            RegisterSync = new RegisterSyncConfiguration
            {
                PeriodicSyncIntervalMinutes = 1,
                MaxRetryAttempts = 3
            },
            PeerDiscovery = new PeerDiscoveryConfiguration(),
            SeedNodes = new SeedNodeConfiguration()
        };

        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(_config));

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock.Object,
            peerListManager,
            Options.Create(_config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        var registerCache = new RegisterCache(
            new Mock<ILogger<RegisterCache>>().Object);

        var replicationService = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            connectionPool,
            peerListManager,
            registerCache);

        _service = new RegisterSyncBackgroundService(
            new Mock<ILogger<RegisterSyncBackgroundService>>().Object,
            replicationService,
            Options.Create(_config));
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_ForwardOnly_CreatesSubscription()
    {
        var sub = await _service.SubscribeToRegisterAsync(
            "reg-1", ReplicationMode.ForwardOnly);

        sub.Should().NotBeNull();
        sub.RegisterId.Should().Be("reg-1");
        sub.Mode.Should().Be(ReplicationMode.ForwardOnly);
        sub.SyncState.Should().Be(RegisterSyncState.Subscribing);
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_FullReplica_CreatesSubscription()
    {
        var sub = await _service.SubscribeToRegisterAsync(
            "reg-1", ReplicationMode.FullReplica);

        sub.Should().NotBeNull();
        sub.RegisterId.Should().Be("reg-1");
        sub.Mode.Should().Be(ReplicationMode.FullReplica);
        sub.SyncState.Should().Be(RegisterSyncState.Subscribing);
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_Duplicate_ReturnsSameSubscription()
    {
        var sub1 = await _service.SubscribeToRegisterAsync(
            "reg-1", ReplicationMode.ForwardOnly);
        var sub2 = await _service.SubscribeToRegisterAsync(
            "reg-1", ReplicationMode.FullReplica);

        sub1.Should().BeSameAs(sub2);
    }

    [Fact]
    public async Task UnsubscribeFromRegisterAsync_ExistingSubscription_Removes()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);

        await _service.UnsubscribeFromRegisterAsync("reg-1");

        _service.GetSubscription("reg-1").Should().BeNull();
    }

    [Fact]
    public async Task UnsubscribeFromRegisterAsync_NonExistent_NoOp()
    {
        // Should not throw
        await _service.UnsubscribeFromRegisterAsync("nonexistent");
    }

    [Fact]
    public async Task GetSubscriptions_ReturnsAllSubscriptions()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);
        await _service.SubscribeToRegisterAsync("reg-2", ReplicationMode.FullReplica);

        var subs = _service.GetSubscriptions();

        subs.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSubscription_ExistingRegister_ReturnsSubscription()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);

        _service.GetSubscription("reg-1").Should().NotBeNull();
    }

    [Fact]
    public void GetSubscription_NonExistent_ReturnsNull()
    {
        _service.GetSubscription("nonexistent").Should().BeNull();
    }

    [Fact]
    public void GetSubscriptions_Empty_ReturnsEmpty()
    {
        _service.GetSubscriptions().Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_MultipleRegisters_TracksAll()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);
        await _service.SubscribeToRegisterAsync("reg-2", ReplicationMode.FullReplica);
        await _service.SubscribeToRegisterAsync("reg-3", ReplicationMode.ForwardOnly);

        _service.GetSubscriptions().Should().HaveCount(3);
        _service.GetSubscription("reg-1")!.Mode.Should().Be(ReplicationMode.ForwardOnly);
        _service.GetSubscription("reg-2")!.Mode.Should().Be(ReplicationMode.FullReplica);
        _service.GetSubscription("reg-3")!.Mode.Should().Be(ReplicationMode.ForwardOnly);
    }

    [Fact]
    public async Task UnsubscribeFromRegisterAsync_OnlyRemovesTarget()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);
        await _service.SubscribeToRegisterAsync("reg-2", ReplicationMode.FullReplica);

        await _service.UnsubscribeFromRegisterAsync("reg-1");

        _service.GetSubscription("reg-1").Should().BeNull();
        _service.GetSubscription("reg-2").Should().NotBeNull();
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}

public class RegisterSyncBackgroundServiceWithDbTests : IDisposable
{
    private readonly IDbContextFactory<PeerDbContext> _dbContextFactory;
    private readonly RegisterSyncBackgroundService _service;

    public RegisterSyncBackgroundServiceWithDbTests()
    {
        var dbName = $"SyncBgService_{Guid.NewGuid()}";
        var optionsBuilder = new DbContextOptionsBuilder<PeerDbContext>()
            .UseInMemoryDatabase(dbName);

        _dbContextFactory = new TestPeerDbContextFactory(optionsBuilder.Options);

        var config = new PeerServiceConfiguration
        {
            NodeId = "test-node",
            RegisterSync = new RegisterSyncConfiguration
            {
                PeriodicSyncIntervalMinutes = 1,
                MaxRetryAttempts = 3
            },
            PeerDiscovery = new PeerDiscoveryConfiguration(),
            SeedNodes = new SeedNodeConfiguration()
        };

        var peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(config));

        var loggerFactoryMock2 = new Mock<ILoggerFactory>();
        loggerFactoryMock2
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var connectionPool = new PeerConnectionPool(
            new Mock<ILogger<PeerConnectionPool>>().Object,
            loggerFactoryMock2.Object,
            peerListManager,
            Options.Create(config),
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());

        var registerCache = new RegisterCache(
            new Mock<ILogger<RegisterCache>>().Object);

        var replicationService = new RegisterReplicationService(
            new Mock<ILogger<RegisterReplicationService>>().Object,
            connectionPool,
            peerListManager,
            registerCache);

        _service = new RegisterSyncBackgroundService(
            new Mock<ILogger<RegisterSyncBackgroundService>>().Object,
            replicationService,
            Options.Create(config),
            _dbContextFactory);
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_PersistsToDatabase()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);

        await using var ctx = await _dbContextFactory.CreateDbContextAsync();
        var entity = await ctx.RegisterSubscriptions
            .FirstOrDefaultAsync(s => s.RegisterId == "reg-1");

        entity.Should().NotBeNull();
        entity!.Mode.Should().Be(ReplicationMode.ForwardOnly);
        entity.SyncState.Should().Be(RegisterSyncState.Subscribing);
    }

    [Fact]
    public async Task UnsubscribeFromRegisterAsync_RemovesFromDatabase()
    {
        await _service.SubscribeToRegisterAsync("reg-1", ReplicationMode.ForwardOnly);
        await _service.UnsubscribeFromRegisterAsync("reg-1");

        await using var ctx = await _dbContextFactory.CreateDbContextAsync();
        var entity = await ctx.RegisterSubscriptions
            .FirstOrDefaultAsync(s => s.RegisterId == "reg-1");

        entity.Should().BeNull();
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    private class TestPeerDbContextFactory : IDbContextFactory<PeerDbContext>
    {
        private readonly DbContextOptions<PeerDbContext> _options;

        public TestPeerDbContextFactory(DbContextOptions<PeerDbContext> options)
        {
            _options = options;
        }

        public PeerDbContext CreateDbContext()
        {
            return new PeerDbContext(_options);
        }
    }
}
