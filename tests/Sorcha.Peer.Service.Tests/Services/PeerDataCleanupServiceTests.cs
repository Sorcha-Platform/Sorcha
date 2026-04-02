// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Replication;
using Sorcha.Peer.Service.Services;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Services;

public class PeerDataCleanupServiceTests : IDisposable
{
    private readonly PeerListManager _peerListManager;
    private readonly IDbContextFactory<PeerDbContext> _dbContextFactory;
    private readonly PeerDataCleanupService _cleanupService;
    private readonly PeerServiceConfiguration _config;

    public PeerDataCleanupServiceTests()
    {
        _config = new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            },
            DataCleanup = new DataCleanupConfiguration
            {
                CleanupIntervalMinutes = 5,
                StalePeerMinutes = 30,
                CompletedTransactionRetentionMinutes = 60,
                CheckpointCleanupIntervalMinutes = 15
            }
        };

        var dbName = $"PeerCleanup_{Guid.NewGuid()}";
        var optionsBuilder = new DbContextOptionsBuilder<PeerDbContext>();
        optionsBuilder.UseInMemoryDatabase(dbName);

        _dbContextFactory = new TestDbContextFactory(optionsBuilder.Options);

        _peerListManager = new PeerListManager(
            Mock.Of<ILogger<PeerListManager>>(),
            Options.Create(_config),
            _dbContextFactory);

        _cleanupService = new PeerDataCleanupService(
            Mock.Of<ILogger<PeerDataCleanupService>>(),
            _peerListManager,
            Options.Create(_config),
            _dbContextFactory);
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_EvictsStalePeers_WhenLastSeenBeyondThreshold()
    {
        // Arrange
        var stalePeer = new PeerNode
        {
            PeerId = "stale-peer",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-60) // 60 min ago, threshold is 30
        };
        var freshPeer = new PeerNode
        {
            PeerId = "fresh-peer",
            Address = "10.0.0.2",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        await _peerListManager.AddOrUpdatePeerAsync(stalePeer);
        await _peerListManager.AddOrUpdatePeerAsync(freshPeer);

        // Act
        await _cleanupService.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert
        _peerListManager.GetPeer("stale-peer").Should().BeNull();
        _peerListManager.GetPeer("fresh-peer").Should().NotBeNull();
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_CleansUpRedisAdvertisementsForEvictedPeers()
    {
        // Arrange
        var mockStore = new Mock<IRedisAdvertisementStore>();
        var cleanupWithRedis = new PeerDataCleanupService(
            Mock.Of<ILogger<PeerDataCleanupService>>(),
            _peerListManager,
            Options.Create(_config),
            _dbContextFactory,
            mockStore.Object);

        var stalePeer = new PeerNode
        {
            PeerId = "stale-peer-redis",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-60)
        };
        await _peerListManager.AddOrUpdatePeerAsync(stalePeer);

        // Act
        await cleanupWithRedis.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert - Redis advertisements should be cleaned up for the evicted peer
        mockStore.Verify(s => s.RemoveRemoteByPeerAsync("stale-peer-redis", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_DoesNotEvictStaleSeedNodes()
    {
        // Arrange
        var staleSeed = new PeerNode
        {
            PeerId = "stale-seed",
            Address = "10.0.0.1",
            Port = 5000,
            IsSeedNode = true,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-120)
        };
        await _peerListManager.AddOrUpdatePeerAsync(staleSeed);

        // Act
        await _cleanupService.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert
        _peerListManager.GetPeer("stale-seed").Should().NotBeNull();
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_UnbansExpiredBans()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "banned-peer",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);
        await _peerListManager.BanPeerAsync("banned-peer", "test ban", TimeSpan.FromMinutes(-1)); // already expired

        // Act
        await _cleanupService.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert
        var result = _peerListManager.GetPeer("banned-peer");
        result.Should().NotBeNull();
        result!.IsBanned.Should().BeFalse();
        result.BanExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_DoesNotUnbanPermanentBans()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "perma-banned",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);
        await _peerListManager.BanPeerAsync("perma-banned", "permanent ban"); // no duration = permanent

        // Act
        await _cleanupService.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert
        var result = _peerListManager.GetPeer("perma-banned");
        result.Should().NotBeNull();
        result!.IsBanned.Should().BeTrue();
        result.BanExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task RunPrimaryCleanupAsync_DoesNotUnbanActiveBans()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "active-ban",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);
        await _peerListManager.BanPeerAsync("active-ban", "test", TimeSpan.FromHours(1)); // expires in 1 hour

        // Act
        await _cleanupService.RunPrimaryCleanupAsync(CancellationToken.None);

        // Assert
        var result = _peerListManager.GetPeer("active-ban");
        result.Should().NotBeNull();
        result!.IsBanned.Should().BeTrue();
        result.BanExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task PurgeQueuedTransactionsAsync_RemovesCompletedPastRetention()
    {
        // Arrange
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.QueuedTransactions.Add(new QueuedTransactionEntity
        {
            Id = Guid.NewGuid(),
            TransactionId = "tx-old-completed",
            RegisterId = "reg-1",
            OriginPeerId = "peer-1",
            DataHash = "abc123",
            Status = "Completed",
            EnqueuedAt = DateTimeOffset.UtcNow.AddHours(-2), // 2 hours ago, retention is 1 hour
            TTL = 86400
        });
        context.QueuedTransactions.Add(new QueuedTransactionEntity
        {
            Id = Guid.NewGuid(),
            TransactionId = "tx-recent-completed",
            RegisterId = "reg-1",
            OriginPeerId = "peer-1",
            DataHash = "def456",
            Status = "Completed",
            EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-10), // 10 min ago, within retention
            TTL = 86400
        });
        await context.SaveChangesAsync();

        // Act
        var purged = await _cleanupService.PurgeQueuedTransactionsAsync(CancellationToken.None);

        // Assert
        purged.Should().BeGreaterThanOrEqualTo(1);
        await using var verifyContext = await _dbContextFactory.CreateDbContextAsync();
        var remaining = await verifyContext.QueuedTransactions.ToListAsync();
        remaining.Should().ContainSingle(t => t.TransactionId == "tx-recent-completed");
    }

    [Fact]
    public async Task PurgeQueuedTransactionsAsync_RemovesTtlExpired()
    {
        // Arrange
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        context.QueuedTransactions.Add(new QueuedTransactionEntity
        {
            Id = Guid.NewGuid(),
            TransactionId = "tx-ttl-expired",
            RegisterId = "reg-1",
            OriginPeerId = "peer-1",
            DataHash = "abc123",
            Status = "Pending",
            EnqueuedAt = DateTimeOffset.UtcNow.AddHours(-2),
            TTL = 60 // 60 second TTL, enqueued 2 hours ago
        });
        await context.SaveChangesAsync();

        // Act
        var purged = await _cleanupService.PurgeQueuedTransactionsAsync(CancellationToken.None);

        // Assert
        purged.Should().BeGreaterThanOrEqualTo(1);
        await using var verifyContext = await _dbContextFactory.CreateDbContextAsync();
        var remaining = await verifyContext.QueuedTransactions.ToListAsync();
        remaining.Should().NotContain(t => t.TransactionId == "tx-ttl-expired");
    }

    [Fact]
    public async Task PurgeOrphanedCheckpointsAsync_RemovesCheckpointsForMissingPeers()
    {
        // Arrange
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        // Add a peer and its checkpoint
        context.Peers.Add(new PeerNodeEntity
        {
            PeerId = "existing-peer",
            Address = "10.0.0.1",
            Port = 5000,
            Protocols = "grpc",
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow
        });
        context.SyncCheckpoints.Add(new SyncCheckpointEntity
        {
            PeerId = "existing-peer",
            RegisterId = "reg-1",
            CurrentVersion = 10,
            NextSyncDue = DateTime.UtcNow
        });

        // Add an orphaned checkpoint (no matching peer)
        context.SyncCheckpoints.Add(new SyncCheckpointEntity
        {
            PeerId = "gone-peer",
            RegisterId = "reg-1",
            CurrentVersion = 5,
            NextSyncDue = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act
        await _cleanupService.PurgeOrphanedCheckpointsAsync(CancellationToken.None);

        // Assert
        await using var verifyContext = await _dbContextFactory.CreateDbContextAsync();
        var checkpoints = await verifyContext.SyncCheckpoints.ToListAsync();
        checkpoints.Should().ContainSingle(c => c.PeerId == "existing-peer");
        checkpoints.Should().NotContain(c => c.PeerId == "gone-peer");
    }

    public void Dispose()
    {
        _peerListManager.Dispose();
    }

    /// <summary>
    /// Simple test implementation of IDbContextFactory for InMemory provider
    /// </summary>
    private sealed class TestDbContextFactory : IDbContextFactory<PeerDbContext>
    {
        private readonly DbContextOptions<PeerDbContext> _options;

        public TestDbContextFactory(DbContextOptions<PeerDbContext> options)
        {
            _options = options;
        }

        public PeerDbContext CreateDbContext() => new(_options);

        public Task<PeerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PeerDbContext(_options));
    }
}
