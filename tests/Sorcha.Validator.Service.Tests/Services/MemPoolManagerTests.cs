// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using StackExchange.Redis;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for MemPoolManager
/// Tests cover >85% code coverage as required by project standards
/// </summary>
public class MemPoolManagerTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<MemPoolManager>> _mockLogger;
    private readonly MemPoolConfiguration _config;
    private readonly IOptions<MemPoolConfiguration> _options;

    // In-memory storage to simulate Redis hash operations
    private readonly Dictionary<string, Dictionary<string, string>> _hashStore = new();

    // In-memory storage to simulate Redis set operations (payload hash index)
    private readonly HashSet<string> _setStore = new();

    public MemPoolManagerTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<MemPoolManager>>();
        _config = new MemPoolConfiguration
        {
            MaxSize = 100,
            DefaultTTL = TimeSpan.FromHours(1),
            HighPriorityQuota = 0.10, // 10%
            CleanupInterval = TimeSpan.FromMinutes(5)
        };
        _options = Options.Create(_config);

        // Wire up IConnectionMultiplexer to return our mock IDatabase
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        // Setup mock IDatabase hash operations backed by in-memory Dictionary
        SetupMockDatabaseHashOperations();
    }

    private void SetupMockDatabaseHashOperations()
    {
        // HashExists
        _mockDatabase
            .Setup(db => db.HashExists(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue field, CommandFlags _) =>
            {
                var k = key.ToString();
                return _hashStore.ContainsKey(k) && _hashStore[k].ContainsKey(field.ToString());
            });

        // HashSet (single field)
        _mockDatabase
            .Setup(db => db.HashSet(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue field, RedisValue value, When _, CommandFlags __) =>
            {
                var k = key.ToString();
                if (!_hashStore.ContainsKey(k))
                    _hashStore[k] = new Dictionary<string, string>();
                var isNew = !_hashStore[k].ContainsKey(field.ToString());
                _hashStore[k][field.ToString()] = value.ToString();
                return isNew;
            });

        // HashDelete (single field)
        _mockDatabase
            .Setup(db => db.HashDelete(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue field, CommandFlags _) =>
            {
                var k = key.ToString();
                if (_hashStore.ContainsKey(k))
                    return _hashStore[k].Remove(field.ToString());
                return false;
            });

        // HashGetAll
        _mockDatabase
            .Setup(db => db.HashGetAll(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, CommandFlags _) =>
            {
                var k = key.ToString();
                if (!_hashStore.ContainsKey(k))
                    return Array.Empty<HashEntry>();
                return _hashStore[k]
                    .Select(kvp => new HashEntry(kvp.Key, kvp.Value))
                    .ToArray();
            });

        // HashLength
        _mockDatabase
            .Setup(db => db.HashLength(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, CommandFlags _) =>
            {
                var k = key.ToString();
                return _hashStore.ContainsKey(k) ? _hashStore[k].Count : 0;
            });

        // HashGet (single field)
        _mockDatabase
            .Setup(db => db.HashGet(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey key, RedisValue field, CommandFlags _) =>
            {
                var k = key.ToString();
                if (_hashStore.ContainsKey(k) && _hashStore[k].TryGetValue(field.ToString(), out var value))
                    return (RedisValue)value;
                return RedisValue.Null;
            });

        // SetAdd
        _mockDatabase
            .Setup(db => db.SetAdd(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey _, RedisValue value, CommandFlags __) => _setStore.Add(value.ToString()));

        // SetContains
        _mockDatabase
            .Setup(db => db.SetContains(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey _, RedisValue value, CommandFlags __) => _setStore.Contains(value.ToString()));

        // SetRemove
        _mockDatabase
            .Setup(db => db.SetRemove(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Returns((RedisKey _, RedisValue value, CommandFlags __) => _setStore.Remove(value.ToString()));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Assert
        manager.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MemPoolManager(_mockRedis.Object, null!, _mockLogger.Object));
        exception.ParamName.Should().Be("config");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MemPoolManager(_mockRedis.Object, _options, null!));
        exception.ParamName.Should().Be("logger");
    }

    #endregion

    #region AddTransactionAsync Tests

    [Fact]
    public async Task AddTransactionAsync_WithValidTransaction_ReturnsTrue()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction(priority: TransactionPriority.Normal);

        // Act
        var result = await manager.AddTransactionAsync("register-1", transaction);

        // Assert
        result.Should().BeTrue();
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(1);
    }

    [Fact]
    public async Task AddTransactionAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.AddTransactionAsync(null!, transaction));
    }

    [Fact]
    public async Task AddTransactionAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.AddTransactionAsync("", transaction));
    }

    [Fact]
    public async Task AddTransactionAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => manager.AddTransactionAsync("register-1", null!));
    }

    [Fact]
    public async Task AddTransactionAsync_WithDuplicateTransaction_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        // Act
        var firstAdd = await manager.AddTransactionAsync("register-1", transaction);
        var secondAdd = await manager.AddTransactionAsync("register-1", transaction);

        // Assert
        firstAdd.Should().BeTrue();
        secondAdd.Should().BeFalse();
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(1);
    }

    [Fact]
    public async Task AddTransactionAsync_WithFullPool_StillAddsTransaction()
    {
        // Arrange - current implementation does not enforce MaxSize or eviction
        var smallConfig = new MemPoolConfiguration { MaxSize = 5, HighPriorityQuota = 0.20 };
        var manager = new MemPoolManager(_mockRedis.Object, Options.Create(smallConfig), _mockLogger.Object);

        // Fill pool beyond max size
        for (int i = 0; i < 5; i++)
        {
            var tx = CreateValidTransaction($"tx-{i}", priority: TransactionPriority.Low);
            await manager.AddTransactionAsync("register-1", tx);
        }

        // Act - Add another transaction (implementation doesn't enforce MaxSize)
        var newTx = CreateValidTransaction("tx-new", priority: TransactionPriority.Normal);
        var result = await manager.AddTransactionAsync("register-1", newTx);

        // Assert - transaction is added (no eviction in current implementation)
        result.Should().BeTrue();
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(6);
    }

    [Fact]
    public async Task AddTransactionAsync_WithHighPriority_AddsSuccessfully()
    {
        // Arrange - current implementation does not enforce quota or downgrade priority
        var config = new MemPoolConfiguration { MaxSize = 100, HighPriorityQuota = 0.10 };
        var manager = new MemPoolManager(_mockRedis.Object, Options.Create(config), _mockLogger.Object);

        // Add high-priority transactions
        for (int i = 0; i < 10; i++)
        {
            var tx = CreateValidTransaction($"tx-high-{i}", priority: TransactionPriority.High);
            await manager.AddTransactionAsync("register-1", tx);
        }

        // Act - Add another high-priority transaction
        var newTx = CreateValidTransaction("tx-high-new", priority: TransactionPriority.High);
        var result = await manager.AddTransactionAsync("register-1", newTx);

        // Assert - transaction is added successfully (no quota enforcement)
        result.Should().BeTrue();
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(11);
    }

    [Fact]
    public async Task AddTransactionAsync_SetsAddedToPoolAt()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction();
        var beforeAdd = DateTimeOffset.UtcNow;

        // Act
        await manager.AddTransactionAsync("register-1", transaction);

        // Assert
        transaction.AddedToPoolAt.Should().BeCloseTo(beforeAdd, precision: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AddTransactionAsync_WithMultipleRegisters_KeepsSeparatePools()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var tx1 = CreateValidTransaction("tx-1");
        var tx2 = CreateValidTransaction("tx-2");

        // Act
        await manager.AddTransactionAsync("register-1", tx1);
        await manager.AddTransactionAsync("register-2", tx2);

        // Assert
        var count1 = await manager.GetTransactionCountAsync("register-1");
        var count2 = await manager.GetTransactionCountAsync("register-2");
        count1.Should().Be(1);
        count2.Should().Be(1);
    }

    #endregion

    #region RemoveTransactionAsync Tests

    [Fact]
    public async Task RemoveTransactionAsync_WithExistingTransaction_ReturnsTrueAndRemoves()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");
        await manager.AddTransactionAsync("register-1", transaction);

        // Act
        var result = await manager.RemoveTransactionAsync("register-1", "tx-1");

        // Assert
        result.Should().BeTrue();
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithNonExistentTransaction_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var result = await manager.RemoveTransactionAsync("register-1", "non-existent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithNonExistentRegister_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var result = await manager.RemoveTransactionAsync("non-existent-register", "tx-1");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetPendingTransactionsAsync Tests

    [Fact]
    public async Task GetPendingTransactionsAsync_WithEmptyPool_ReturnsEmptyList()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var transactions = await manager.GetPendingTransactionsAsync("register-1", 10);

        // Assert
        transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPendingTransactionsAsync_ReturnsAllTransactions()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var lowTx = CreateValidTransaction("tx-low", priority: TransactionPriority.Low);
        var normalTx = CreateValidTransaction("tx-normal", priority: TransactionPriority.Normal);
        var highTx = CreateValidTransaction("tx-high", priority: TransactionPriority.High);

        await manager.AddTransactionAsync("register-1", lowTx);
        await manager.AddTransactionAsync("register-1", normalTx);
        await manager.AddTransactionAsync("register-1", highTx);

        // Act
        var transactions = await manager.GetPendingTransactionsAsync("register-1", 10);

        // Assert - current implementation returns transactions without priority ordering
        transactions.Should().HaveCount(3);
        transactions.Select(t => t.TransactionId).Should()
            .Contain("tx-high")
            .And.Contain("tx-normal")
            .And.Contain("tx-low");
    }

    [Fact]
    public async Task GetPendingTransactionsAsync_RespectsMaxCount()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Add 10 transactions
        for (int i = 0; i < 10; i++)
        {
            var tx = CreateValidTransaction($"tx-{i}");
            await manager.AddTransactionAsync("register-1", tx);
        }

        // Act
        var transactions = await manager.GetPendingTransactionsAsync("register-1", 5);

        // Assert
        transactions.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetPendingTransactionsAsync_ReturnsAddedTransactions()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var tx1 = CreateValidTransaction("tx-1", priority: TransactionPriority.Normal);
        var tx2 = CreateValidTransaction("tx-2", priority: TransactionPriority.Normal);
        var tx3 = CreateValidTransaction("tx-3", priority: TransactionPriority.Normal);

        await manager.AddTransactionAsync("register-1", tx1);
        await manager.AddTransactionAsync("register-1", tx2);
        await manager.AddTransactionAsync("register-1", tx3);

        // Act
        var transactions = await manager.GetPendingTransactionsAsync("register-1", 10);

        // Assert
        transactions.Should().HaveCount(3);
        transactions.Select(t => t.TransactionId).Should()
            .Contain("tx-1")
            .And.Contain("tx-2")
            .And.Contain("tx-3");
    }

    #endregion

    #region GetTransactionCountAsync Tests

    [Fact]
    public async Task GetTransactionCountAsync_WithEmptyPool_ReturnsZero()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var count = await manager.GetTransactionCountAsync("register-1");

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetTransactionCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        for (int i = 0; i < 5; i++)
        {
            var tx = CreateValidTransaction($"tx-{i}");
            await manager.AddTransactionAsync("register-1", tx);
        }

        // Act
        var count = await manager.GetTransactionCountAsync("register-1");

        // Assert
        count.Should().Be(5);
    }

    #endregion

    #region GetStatsAsync Tests

    [Fact]
    public async Task GetStatsAsync_WithEmptyPool_ReturnsZeroStats()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var stats = await manager.GetStatsAsync("register-1");

        // Assert
        stats.RegisterId.Should().Be("register-1");
        stats.TotalTransactions.Should().Be(0);
        stats.MaxSize.Should().Be(_config.MaxSize);
        stats.FillPercentage.Should().Be(0);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsTotalTransactionCount()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var highTx = CreateValidTransaction("tx-high", priority: TransactionPriority.High);
        var normalTx1 = CreateValidTransaction("tx-normal-1", priority: TransactionPriority.Normal);
        var normalTx2 = CreateValidTransaction("tx-normal-2", priority: TransactionPriority.Normal);
        var lowTx = CreateValidTransaction("tx-low", priority: TransactionPriority.Low);

        await manager.AddTransactionAsync("register-1", highTx);
        await manager.AddTransactionAsync("register-1", normalTx1);
        await manager.AddTransactionAsync("register-1", normalTx2);
        await manager.AddTransactionAsync("register-1", lowTx);

        // Act
        var stats = await manager.GetStatsAsync("register-1");

        // Assert - current implementation only tracks TotalTransactions, not per-priority counts
        stats.TotalTransactions.Should().Be(4);
        stats.FillPercentage.Should().Be(4.0); // 4/100 * 100
    }

    #endregion

    #region TransactionExistsAsync Tests

    [Fact]
    public async Task TransactionExistsAsync_WithExistingPayloadHash_ReturnsTrue()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");
        await manager.AddTransactionAsync("register-1", transaction);

        // Act
        var exists = await manager.TransactionExistsAsync(transaction.PayloadHash);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task TransactionExistsAsync_WithNonExistentPayloadHash_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var exists = await manager.TransactionExistsAsync("non-existent-hash");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task TransactionExistsAsync_WithNullOrWhiteSpace_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        (await manager.TransactionExistsAsync(null!)).Should().BeFalse();
        (await manager.TransactionExistsAsync("")).Should().BeFalse();
        (await manager.TransactionExistsAsync("   ")).Should().BeFalse();
    }

    [Fact]
    public async Task TransactionExistsAsync_AfterRemoval_ReturnsFalse()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");
        await manager.AddTransactionAsync("register-1", transaction);

        // Verify it exists first
        (await manager.TransactionExistsAsync(transaction.PayloadHash)).Should().BeTrue();

        // Act
        await manager.RemoveTransactionAsync("register-1", "tx-1");

        // Assert
        (await manager.TransactionExistsAsync(transaction.PayloadHash)).Should().BeFalse();
    }

    #endregion

    #region CleanupExpiredTransactionsAsync Tests

    [Fact]
    public async Task CleanupExpiredTransactionsAsync_RemovesExpiredTransactions()
    {
        // Arrange - use short TTL config so transactions expire quickly
        var shortTtlConfig = new MemPoolConfiguration
        {
            MaxSize = 100,
            DefaultTTL = TimeSpan.FromMilliseconds(1), // Very short TTL
            HighPriorityQuota = 0.10,
            CleanupInterval = TimeSpan.FromMinutes(5)
        };
        var manager = new MemPoolManager(_mockRedis.Object, Options.Create(shortTtlConfig), _mockLogger.Object);

        var tx1 = CreateValidTransaction("tx-expired");
        await manager.AddTransactionAsync("register-1", tx1);

        // Wait for TTL to expire
        await Task.Delay(50);

        // Setup server.Keys() to return our hash keys
        var mockServer = new Mock<IServer>();
        var hashKey = "validator:mempool:register-1:transactions";
        mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new RedisKey[] { hashKey });
        _mockRedis.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _mockRedis.Setup(r => r.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>()))
            .Returns(mockServer.Object);

        // Act
        await manager.CleanupExpiredTransactionsAsync();

        // Assert
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredTransactionsAsync_WithNoExpiredTransactions_DoesNothing()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var validTx1 = CreateValidTransaction("tx-1");
        var validTx2 = CreateValidTransaction("tx-2");

        await manager.AddTransactionAsync("register-1", validTx1);
        await manager.AddTransactionAsync("register-1", validTx2);

        // Setup server.Keys()
        var mockServer = new Mock<IServer>();
        var hashKey = "validator:mempool:register-1:transactions";
        mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new RedisKey[] { hashKey });
        _mockRedis.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _mockRedis.Setup(r => r.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>()))
            .Returns(mockServer.Object);

        // Act
        await manager.CleanupExpiredTransactionsAsync();

        // Assert - 1 hour TTL means transactions are not expired
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(2);
    }

    [Fact]
    public async Task CleanupExpiredTransactionsAsync_WithMultipleRegisters_CleansAll()
    {
        // Arrange - use short TTL
        var shortTtlConfig = new MemPoolConfiguration
        {
            MaxSize = 100,
            DefaultTTL = TimeSpan.FromMilliseconds(1),
            HighPriorityQuota = 0.10,
            CleanupInterval = TimeSpan.FromMinutes(5)
        };
        var manager = new MemPoolManager(_mockRedis.Object, Options.Create(shortTtlConfig), _mockLogger.Object);

        var expiredTx1 = CreateValidTransaction("tx-expired-1");
        var expiredTx2 = CreateValidTransaction("tx-expired-2");

        await manager.AddTransactionAsync("register-1", expiredTx1);
        await manager.AddTransactionAsync("register-2", expiredTx2);

        await Task.Delay(50);

        // Setup server.Keys()
        var mockServer = new Mock<IServer>();
        mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(new RedisKey[]
            {
                "validator:mempool:register-1:transactions",
                "validator:mempool:register-2:transactions"
            });
        _mockRedis.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _mockRedis.Setup(r => r.GetServer(It.IsAny<System.Net.EndPoint>(), It.IsAny<object>()))
            .Returns(mockServer.Object);

        // Act
        await manager.CleanupExpiredTransactionsAsync();

        // Assert
        var count1 = await manager.GetTransactionCountAsync("register-1");
        var count2 = await manager.GetTransactionCountAsync("register-2");
        count1.Should().Be(0);
        count2.Should().Be(0);
    }

    #endregion

    #region ReturnTransactionsAsync Tests

    [Fact]
    public async Task ReturnTransactionsAsync_WithValidTransactions_AddsThemBack()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var tx1 = CreateValidTransaction("tx-1");
        var tx2 = CreateValidTransaction("tx-2");
        var transactions = new List<Transaction> { tx1, tx2 };

        // Act
        await manager.ReturnTransactionsAsync("register-1", transactions);

        // Assert
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(2);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_WithNullList_DoesNothing()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        await manager.ReturnTransactionsAsync("register-1", null!);

        // Assert
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(0);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_WithEmptyList_DoesNothing()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        await manager.ReturnTransactionsAsync("register-1", new List<Transaction>());

        // Assert
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(0);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_PreservesOriginalPriority()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);

        var tx = CreateValidTransaction("tx-1", priority: TransactionPriority.High);
        var transactions = new List<Transaction> { tx };

        // Act
        await manager.ReturnTransactionsAsync("register-1", transactions);

        // Assert
        var retrieved = await manager.GetPendingTransactionsAsync("register-1", 10);
        retrieved.Should().HaveCount(1);
        retrieved[0].Priority.Should().Be(TransactionPriority.High);
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public async Task MemPoolManager_WithHighConcurrency_HandlesCorrectly()
    {
        // Arrange
        var manager = new MemPoolManager(_mockRedis.Object, _options, _mockLogger.Object);
        var tasks = new List<Task<bool>>();

        // Act - Add 50 transactions concurrently
        for (int i = 0; i < 50; i++)
        {
            var tx = CreateValidTransaction($"tx-{i}");
            tasks.Add(manager.AddTransactionAsync("register-1", tx));
        }

        await Task.WhenAll(tasks);

        // Assert
        var count = await manager.GetTransactionCountAsync("register-1");
        count.Should().Be(50);
        tasks.All(t => t.Result).Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a valid transaction for testing
    /// </summary>
    private static Transaction CreateValidTransaction(
        string? transactionId = null,
        TransactionPriority priority = TransactionPriority.Normal,
        DateTimeOffset? expiresAt = null)
    {
        return new Transaction
        {
            TransactionId = transactionId ?? $"tx-{Guid.NewGuid()}",
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "action-1",
            Payload = JsonDocument.Parse("{\"data\":\"test\"}").RootElement,
            PayloadHash = "abc123def456",
            Signatures = new List<Signature>
            {
                new Signature
                {
                    PublicKey = System.Text.Encoding.UTF8.GetBytes("public-key-1"),
                    SignatureValue = System.Text.Encoding.UTF8.GetBytes("signature-value-1"),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt,
            Priority = priority
        };
    }

    #endregion
}
