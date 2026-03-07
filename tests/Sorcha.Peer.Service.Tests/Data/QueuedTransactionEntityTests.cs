// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Distribution;

namespace Sorcha.Peer.Service.Tests.Data;

public class QueuedTransactionEntityTests : IDisposable
{
    private readonly DbContextOptions<PeerDbContext> _options;

    public QueuedTransactionEntityTests()
    {
        _options = new DbContextOptionsBuilder<PeerDbContext>()
            .UseInMemoryDatabase($"QueuedTxTestDb_{Guid.NewGuid()}")
            .Options;
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Act
        var entity = new QueuedTransactionEntity();

        // Assert
        entity.Id.Should().NotBe(Guid.Empty);
        entity.TransactionId.Should().BeEmpty();
        entity.RegisterId.Should().BeEmpty();
        entity.OriginPeerId.Should().BeEmpty();
        entity.DataHash.Should().BeEmpty();
        entity.TTL.Should().Be(3600);
        entity.HasFullData.Should().BeFalse();
        entity.RetryCount.Should().Be(0);
        entity.Status.Should().Be(QueueStatus.Pending.ToString());
        entity.EnqueuedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        entity.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        entity.LastAttemptAt.Should().BeNull();
        entity.TransactionData.Should().BeNull();
    }

    [Fact]
    public void ToDomain_MapsAllFields()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new QueuedTransactionEntity
        {
            Id = id,
            TransactionId = "tx-001",
            RegisterId = "reg-001",
            OriginPeerId = "peer-1",
            Timestamp = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            DataSize = 1024,
            DataHash = "abc123",
            GossipRound = 2,
            HopCount = 3,
            TTL = 7200,
            HasFullData = true,
            TransactionData = new byte[] { 1, 2, 3 },
            EnqueuedAt = new DateTimeOffset(2026, 1, 15, 12, 0, 1, TimeSpan.Zero),
            RetryCount = 1,
            LastAttemptAt = new DateTimeOffset(2026, 1, 15, 12, 1, 0, TimeSpan.Zero),
            Status = QueueStatus.Pending.ToString()
        };

        // Act
        var domain = entity.ToDomain();

        // Assert
        domain.Id.Should().Be(id.ToString());
        domain.Transaction.TransactionId.Should().Be("tx-001");
        domain.Transaction.RegisterId.Should().Be("reg-001");
        domain.Transaction.OriginPeerId.Should().Be("peer-1");
        domain.Transaction.Timestamp.Should().Be(entity.Timestamp);
        domain.Transaction.DataSize.Should().Be(1024);
        domain.Transaction.DataHash.Should().Be("abc123");
        domain.Transaction.GossipRound.Should().Be(2);
        domain.Transaction.HopCount.Should().Be(3);
        domain.Transaction.TTL.Should().Be(7200);
        domain.Transaction.HasFullData.Should().BeTrue();
        domain.Transaction.TransactionData.Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        domain.EnqueuedAt.Should().Be(entity.EnqueuedAt);
        domain.RetryCount.Should().Be(1);
        domain.LastAttemptAt.Should().Be(entity.LastAttemptAt);
        domain.Status.Should().Be(QueueStatus.Pending);
    }

    [Fact]
    public void FromDomain_MapsAllFields()
    {
        // Arrange
        var id = Guid.NewGuid();
        var queued = new QueuedTransaction
        {
            Id = id.ToString(),
            Transaction = new TransactionNotification
            {
                TransactionId = "tx-002",
                RegisterId = "reg-002",
                OriginPeerId = "peer-2",
                Timestamp = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                DataSize = 2048,
                DataHash = "def456",
                GossipRound = 1,
                HopCount = 0,
                TTL = 3600,
                HasFullData = false,
                TransactionData = null
            },
            EnqueuedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 1, TimeSpan.Zero),
            RetryCount = 3,
            LastAttemptAt = new DateTimeOffset(2026, 2, 1, 0, 5, 0, TimeSpan.Zero),
            Status = QueueStatus.Failed
        };

        // Act
        var entity = QueuedTransactionEntity.FromDomain(queued);

        // Assert
        entity.Id.Should().Be(id);
        entity.TransactionId.Should().Be("tx-002");
        entity.RegisterId.Should().Be("reg-002");
        entity.OriginPeerId.Should().Be("peer-2");
        entity.DataSize.Should().Be(2048);
        entity.DataHash.Should().Be("def456");
        entity.GossipRound.Should().Be(1);
        entity.HopCount.Should().Be(0);
        entity.TTL.Should().Be(3600);
        entity.HasFullData.Should().BeFalse();
        entity.TransactionData.Should().BeNull();
        entity.EnqueuedAt.Should().Be(queued.EnqueuedAt);
        entity.RetryCount.Should().Be(3);
        entity.LastAttemptAt.Should().Be(queued.LastAttemptAt);
        entity.Status.Should().Be(QueueStatus.Failed.ToString());
    }

    [Fact]
    public void FromDomain_NonGuidId_GeneratesNewGuid()
    {
        // Arrange
        var queued = new QueuedTransaction
        {
            Id = "not-a-guid",
            Transaction = new TransactionNotification
            {
                TransactionId = "tx-003",
                RegisterId = "reg-003",
                OriginPeerId = "peer-3",
                DataHash = "ghi789"
            },
            Status = QueueStatus.Pending
        };

        // Act
        var entity = QueuedTransactionEntity.FromDomain(queued);

        // Assert
        entity.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        // Arrange
        var original = new QueuedTransaction
        {
            Id = Guid.NewGuid().ToString(),
            Transaction = new TransactionNotification
            {
                TransactionId = "tx-round",
                RegisterId = "reg-round",
                OriginPeerId = "peer-round",
                Timestamp = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
                DataSize = 512,
                DataHash = "roundhash",
                GossipRound = 5,
                HopCount = 2,
                TTL = 1800,
                HasFullData = true,
                TransactionData = new byte[] { 10, 20, 30, 40 }
            },
            EnqueuedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 1, TimeSpan.Zero),
            RetryCount = 2,
            LastAttemptAt = new DateTimeOffset(2026, 3, 1, 10, 5, 0, TimeSpan.Zero),
            Status = QueueStatus.Processing
        };

        // Act
        var entity = QueuedTransactionEntity.FromDomain(original);
        var restored = entity.ToDomain();

        // Assert
        restored.Transaction.TransactionId.Should().Be(original.Transaction.TransactionId);
        restored.Transaction.RegisterId.Should().Be(original.Transaction.RegisterId);
        restored.Transaction.OriginPeerId.Should().Be(original.Transaction.OriginPeerId);
        restored.Transaction.DataSize.Should().Be(original.Transaction.DataSize);
        restored.Transaction.DataHash.Should().Be(original.Transaction.DataHash);
        restored.Transaction.GossipRound.Should().Be(original.Transaction.GossipRound);
        restored.Transaction.HopCount.Should().Be(original.Transaction.HopCount);
        restored.Transaction.TTL.Should().Be(original.Transaction.TTL);
        restored.Transaction.HasFullData.Should().Be(original.Transaction.HasFullData);
        restored.Transaction.TransactionData.Should().BeEquivalentTo(original.Transaction.TransactionData);
        restored.EnqueuedAt.Should().Be(original.EnqueuedAt);
        restored.RetryCount.Should().Be(original.RetryCount);
        restored.Status.Should().Be(original.Status);
    }

    [Fact]
    public async Task Entity_CanBePersistedAndRetrieved()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity = new QueuedTransactionEntity
        {
            Id = id,
            TransactionId = "tx-persist",
            RegisterId = "reg-persist",
            OriginPeerId = "peer-persist",
            DataSize = 256,
            DataHash = "persisthash",
            GossipRound = 1,
            HopCount = 0,
            TTL = 3600,
            HasFullData = false,
            RetryCount = 0,
            Status = QueueStatus.Pending.ToString()
        };

        // Act - Save
        await using (var context = new PeerDbContext(_options))
        {
            context.QueuedTransactions.Add(entity);
            await context.SaveChangesAsync();
        }

        // Assert - Retrieve
        await using (var context = new PeerDbContext(_options))
        {
            var retrieved = await context.QueuedTransactions.FindAsync(id);
            retrieved.Should().NotBeNull();
            retrieved!.TransactionId.Should().Be("tx-persist");
            retrieved.RegisterId.Should().Be("reg-persist");
            retrieved.OriginPeerId.Should().Be("peer-persist");
            retrieved.DataSize.Should().Be(256);
            retrieved.DataHash.Should().Be("persisthash");
            retrieved.Status.Should().Be(QueueStatus.Pending.ToString());
        }
    }

    [Fact]
    public async Task Entity_CanBeQueried_ByStatus()
    {
        // Arrange
        await using (var context = new PeerDbContext(_options))
        {
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-pending",
                RegisterId = "reg-1",
                OriginPeerId = "peer-1",
                DataHash = "hash1",
                Status = QueueStatus.Pending.ToString()
            });
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-failed",
                RegisterId = "reg-1",
                OriginPeerId = "peer-1",
                DataHash = "hash2",
                Status = QueueStatus.Failed.ToString()
            });
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-pending-2",
                RegisterId = "reg-2",
                OriginPeerId = "peer-2",
                DataHash = "hash3",
                Status = QueueStatus.Pending.ToString()
            });
            await context.SaveChangesAsync();
        }

        // Act
        await using (var context = new PeerDbContext(_options))
        {
            var pending = await context.QueuedTransactions
                .Where(e => e.Status == QueueStatus.Pending.ToString())
                .ToListAsync();

            // Assert
            pending.Should().HaveCount(2);
            pending.Select(e => e.TransactionId).Should().Contain(["tx-pending", "tx-pending-2"]);
        }
    }

    public void Dispose()
    {
        // InMemory databases are automatically cleaned up
    }
}
