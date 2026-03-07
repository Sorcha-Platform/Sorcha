// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Distribution;

namespace Sorcha.Peer.Service.Tests.Distribution;

public class TransactionQueueManagerPgTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PeerServiceConfiguration> _config;
    private readonly Mock<ILogger<TransactionQueueManager>> _loggerMock;
    private readonly string _dbName;

    public TransactionQueueManagerPgTests()
    {
        _dbName = $"TxQueueTestDb_{Guid.NewGuid()}";
        _loggerMock = new Mock<ILogger<TransactionQueueManager>>();

        var services = new ServiceCollection();
        services.AddDbContext<PeerDbContext>(options =>
            options.UseInMemoryDatabase(_dbName));
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _config = Options.Create(new PeerServiceConfiguration
        {
            OfflineMode = new OfflineModeConfiguration
            {
                QueuePersistence = true,
                MaxQueueSize = 100,
                MaxRetries = 3
            }
        });
    }

    private TransactionQueueManager CreateManager(IOptions<PeerServiceConfiguration>? config = null)
    {
        return new TransactionQueueManager(
            _loggerMock.Object,
            config ?? _config,
            _scopeFactory);
    }

    private static TransactionNotification CreateNotification(string txId = "tx-001", string registerId = "reg-001")
    {
        return new TransactionNotification
        {
            TransactionId = txId,
            RegisterId = registerId,
            OriginPeerId = "peer-1",
            DataSize = 256,
            DataHash = "abc123",
            GossipRound = 1,
            HopCount = 0,
            TTL = 3600,
            HasFullData = false
        };
    }

    [Fact]
    public async Task EnqueueAsync_PersistsToDatabase()
    {
        // Arrange
        var manager = CreateManager();
        var notification = CreateNotification();

        // Act
        var result = await manager.EnqueueAsync(notification);

        // Assert
        result.Should().BeTrue();
        manager.GetQueueSize().Should().Be(1);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
        var entities = await context.QueuedTransactions.ToListAsync();
        entities.Should().HaveCount(1);
        entities[0].TransactionId.Should().Be("tx-001");
        entities[0].RegisterId.Should().Be("reg-001");
        entities[0].Status.Should().Be(QueueStatus.Pending.ToString());
    }

    [Fact]
    public async Task EnqueueAsync_RejectsWhenQueueFull()
    {
        // Arrange
        var smallConfig = Options.Create(new PeerServiceConfiguration
        {
            OfflineMode = new OfflineModeConfiguration
            {
                QueuePersistence = true,
                MaxQueueSize = 2,
                MaxRetries = 3
            }
        });
        var manager = CreateManager(smallConfig);

        await manager.EnqueueAsync(CreateNotification("tx-1"));
        await manager.EnqueueAsync(CreateNotification("tx-2"));

        // Act
        var result = await manager.EnqueueAsync(CreateNotification("tx-3"));

        // Assert
        result.Should().BeFalse();
        manager.GetQueueSize().Should().Be(2);
    }

    [Fact]
    public async Task EnqueueAsync_ThrowsOnNullTransaction()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        var act = () => manager.EnqueueAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task MarkAsProcessedAsync_RemovesFromDatabase()
    {
        // Arrange
        var manager = CreateManager();
        var notification = CreateNotification();
        await manager.EnqueueAsync(notification);

        // Get the ID from the database
        string entityId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            var entity = await context.QueuedTransactions.FirstAsync();
            entityId = entity.Id.ToString();
        }

        // Act
        await manager.MarkAsProcessedAsync(entityId);

        // Assert
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            var remaining = await context.QueuedTransactions.CountAsync();
            remaining.Should().Be(0);
        }
    }

    [Fact]
    public async Task MarkAsFailedAsync_RequeuesIfRetriesRemain()
    {
        // Arrange
        var manager = CreateManager();
        await manager.EnqueueAsync(CreateNotification());
        manager.TryDequeue(out var queuedTx);

        // Act
        var result = await manager.MarkAsFailedAsync(queuedTx!);

        // Assert
        result.Should().BeTrue();
        queuedTx!.RetryCount.Should().Be(1);
        queuedTx.Status.Should().Be(QueueStatus.Pending);
        manager.GetQueueSize().Should().Be(1);
    }

    [Fact]
    public async Task MarkAsFailedAsync_DropsAfterMaxRetries()
    {
        // Arrange
        var manager = CreateManager();
        await manager.EnqueueAsync(CreateNotification());
        manager.TryDequeue(out var queuedTx);

        // Exhaust retries
        queuedTx!.RetryCount = 2; // MaxRetries is 3

        // Act
        var result = await manager.MarkAsFailedAsync(queuedTx);

        // Assert
        result.Should().BeFalse();
        queuedTx.Status.Should().Be(QueueStatus.Failed);
    }

    [Fact]
    public async Task MarkAsFailedAsync_ReturnsfalseForNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.MarkAsFailedAsync(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task LoadFromDatabaseAsync_RestoresExistingRecords()
    {
        // Arrange - Insert directly into DB
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-preexisting-1",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash1",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-preexisting-2",
                RegisterId = "reg-002",
                OriginPeerId = "peer-2",
                DataHash = "hash2",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            });
            // This one should NOT be loaded (Failed status)
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-failed",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash3",
                Status = QueueStatus.Failed.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
            await context.SaveChangesAsync();
        }

        // Act
        var manager = CreateManager();
        await manager.LoadFromDatabaseAsync();

        // Assert
        manager.GetQueueSize().Should().Be(2);

        manager.TryDequeue(out var first);
        first!.Transaction.TransactionId.Should().Be("tx-preexisting-1");

        manager.TryDequeue(out var second);
        second!.Transaction.TransactionId.Should().Be("tx-preexisting-2");
    }

    [Fact]
    public async Task LoadFromDatabaseAsync_OrdersByEnqueuedAt()
    {
        // Arrange
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-newer",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash1",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow
            });
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-older",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash2",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            await context.SaveChangesAsync();
        }

        // Act
        var manager = CreateManager();
        await manager.LoadFromDatabaseAsync();

        // Assert - older should come first
        manager.TryDequeue(out var first);
        first!.Transaction.TransactionId.Should().Be("tx-older");
    }

    [Fact]
    public async Task LoadFromDatabaseAsync_SkipsWhenPersistenceDisabled()
    {
        // Arrange
        var noPersistConfig = Options.Create(new PeerServiceConfiguration
        {
            OfflineMode = new OfflineModeConfiguration
            {
                QueuePersistence = false,
                MaxQueueSize = 100,
                MaxRetries = 3
            }
        });

        // Insert data that should NOT be loaded
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-should-not-load",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash1",
                Status = QueueStatus.Pending.ToString()
            });
            await context.SaveChangesAsync();
        }

        // Act
        var manager = CreateManager(noPersistConfig);
        await manager.LoadFromDatabaseAsync();

        // Assert
        manager.GetQueueSize().Should().Be(0);
    }

    [Fact]
    public async Task TryDequeue_ReturnsEnqueuedTransaction()
    {
        // Arrange
        var manager = CreateManager();
        await manager.EnqueueAsync(CreateNotification("tx-dequeue"));

        // Act
        var result = manager.TryDequeue(out var transaction);

        // Assert
        result.Should().BeTrue();
        transaction.Should().NotBeNull();
        transaction!.Transaction.TransactionId.Should().Be("tx-dequeue");
    }

    [Fact]
    public void TryDequeue_ReturnsFalseWhenEmpty()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = manager.TryDequeue(out var transaction);

        // Assert
        result.Should().BeFalse();
        transaction.Should().BeNull();
    }

    [Fact]
    public void IsEmpty_ReturnsTrueForNewManager()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        manager.IsEmpty().Should().BeTrue();
        manager.GetQueueSize().Should().Be(0);
    }

    [Fact]
    public async Task Constructor_WithoutScopeFactory_WorksInMemoryOnly()
    {
        // Arrange - no scope factory, persistence enabled
        var manager = new TransactionQueueManager(
            _loggerMock.Object,
            _config,
            scopeFactory: null);

        // Act - should work for in-memory operations
        await manager.EnqueueAsync(CreateNotification());

        // Assert
        manager.GetQueueSize().Should().Be(1);
        manager.TryDequeue(out var tx);
        tx.Should().NotBeNull();
    }

    [Fact]
    public async Task MaxQueueSize_TrimsOldestFromDatabase()
    {
        // Arrange
        var smallConfig = Options.Create(new PeerServiceConfiguration
        {
            OfflineMode = new OfflineModeConfiguration
            {
                QueuePersistence = true,
                MaxQueueSize = 2,
                MaxRetries = 3
            }
        });

        // Seed 2 existing DB entries (at max capacity)
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-old",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash1",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            });
            context.QueuedTransactions.Add(new QueuedTransactionEntity
            {
                TransactionId = "tx-recent",
                RegisterId = "reg-001",
                OriginPeerId = "peer-1",
                DataHash = "hash2",
                Status = QueueStatus.Pending.ToString(),
                EnqueuedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            await context.SaveChangesAsync();
        }

        // Create manager with fresh in-memory queue (not loaded from DB)
        var manager = CreateManager(smallConfig);

        // Act - enqueue a new one (in-memory queue is empty, so it will accept)
        await manager.EnqueueAsync(CreateNotification("tx-new"));

        // Assert - DB should have trimmed oldest to stay at max 2
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PeerDbContext>();
            var count = await context.QueuedTransactions.CountAsync();
            count.Should().Be(2);

            // The oldest should have been removed
            var remaining = await context.QueuedTransactions.OrderBy(e => e.EnqueuedAt).ToListAsync();
            remaining.Select(e => e.TransactionId).Should().NotContain("tx-old");
        }
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}
