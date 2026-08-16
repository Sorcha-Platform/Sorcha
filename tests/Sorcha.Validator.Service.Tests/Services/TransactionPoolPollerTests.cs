// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using StackExchange.Redis;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for TransactionPoolPoller
/// Tests cover Redis-backed transaction pool operations including
/// normal operations, exception handling, configuration, and edge cases.
/// </summary>
public class TransactionPoolPollerTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<TransactionPoolPoller>> _mockLogger;
    private readonly TransactionPoolPollerConfiguration _config;
    private readonly IOptions<TransactionPoolPollerConfiguration> _options;

    public TransactionPoolPollerTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<TransactionPoolPoller>>();

        _mockRedis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _config = new TransactionPoolPollerConfiguration
        {
            KeyPrefix = "test:validator:unverified:",
            BatchSize = 50,
            PollingInterval = TimeSpan.FromMilliseconds(100),
            TransactionTtl = TimeSpan.FromHours(1),
            MaxRetries = 3,
            Enabled = true
        };
        _options = Options.Create(_config);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Assert
        poller.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullRedis_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TransactionPoolPoller(null!, _options, _mockLogger.Object));
        exception.ParamName.Should().Be("redis");
    }

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TransactionPoolPoller(_mockRedis.Object, null!, _mockLogger.Object));
        exception.ParamName.Should().Be("config");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new TransactionPoolPoller(_mockRedis.Object, _options, null!));
        exception.ParamName.Should().Be("logger");
    }

    #endregion

    #region SubmitTransactionAsync Tests

    [Fact]
    public async Task SubmitTransactionAsync_WithValidTransaction_ReturnsTrue()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithExistingTransaction_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.SubmitTransactionAsync(null!, transaction));
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.SubmitTransactionAsync("", transaction));
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.SubmitTransactionAsync("register-1", null!));
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithExpiredTransaction_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitTransactionAsync_WhenRedisTransactionFails_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Redis transaction commit fails
        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitTransactionAsync_WithWhitespaceRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.SubmitTransactionAsync("   ", transaction));
    }

    #endregion

    #region PollTransactionsAsync Tests

    [Fact]
    public async Task PollTransactionsAsync_WithEmptyQueue_ReturnsEmptyList()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((SortedSetEntry?)null);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PollTransactionsAsync_WithTransactions_ReturnsTransactions()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");
        var json = JsonSerializer.Serialize(transaction, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var callCount = 0;
        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? new SortedSetEntry("tx-1", 0) : (SortedSetEntry?)null;
            });

        _mockDatabase.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 10);

        // Assert
        result.Should().HaveCount(1);
        result[0].TransactionId.Should().Be("tx-1");
    }

    [Fact]
    public async Task PollTransactionsAsync_WithZeroMaxCount_ReturnsEmpty()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 0);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PollTransactionsAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.PollTransactionsAsync(null!, 10));
    }

    [Fact]
    public async Task PollTransactionsAsync_WithNegativeMaxCount_ReturnsEmpty()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", -1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PollTransactionsAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.PollTransactionsAsync("", 10));
    }

    [Fact]
    public async Task PollTransactionsAsync_WhenDataExpiredMidPoll_SkipsAndContinues()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction2 = CreateValidTransaction("tx-2");
        var json2 = JsonSerializer.Serialize(transaction2, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var popCallCount = 0;
        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                popCallCount++;
                return popCallCount switch
                {
                    1 => new SortedSetEntry("tx-1", 0),  // First tx - data will be expired
                    2 => new SortedSetEntry("tx-2", 0),  // Second tx - data available
                    _ => (SortedSetEntry?)null
                };
            });

        var getCallCount = 0;
        _mockDatabase.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                getCallCount++;
                // First call: data expired (null), second call: data available
                return getCallCount == 1 ? RedisValue.Null : (RedisValue)json2;
            });

        _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 10);

        // Assert - should skip tx-1 (expired) and return tx-2
        result.Should().HaveCount(1);
        result[0].TransactionId.Should().Be("tx-2");
    }

    [Fact]
    public async Task PollTransactionsAsync_WithMultipleTransactions_ReturnsAll()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var tx1 = CreateValidTransaction("tx-1");
        var tx2 = CreateValidTransaction("tx-2");
        var tx3 = CreateValidTransaction("tx-3");
        var json1 = JsonSerializer.Serialize(tx1, jsonOptions);
        var json2 = JsonSerializer.Serialize(tx2, jsonOptions);
        var json3 = JsonSerializer.Serialize(tx3, jsonOptions);

        var popCallCount = 0;
        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                popCallCount++;
                return popCallCount switch
                {
                    1 => new SortedSetEntry("tx-1", 0),
                    2 => new SortedSetEntry("tx-2", 0),
                    3 => new SortedSetEntry("tx-3", 0),
                    _ => (SortedSetEntry?)null
                };
            });

        var getCallCount = 0;
        _mockDatabase.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                getCallCount++;
                return getCallCount switch
                {
                    1 => (RedisValue)json1,
                    2 => (RedisValue)json2,
                    3 => (RedisValue)json3,
                    _ => RedisValue.Null
                };
            });

        _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 10);

        // Assert
        result.Should().HaveCount(3);
        result[0].TransactionId.Should().Be("tx-1");
        result[1].TransactionId.Should().Be("tx-2");
        result[2].TransactionId.Should().Be("tx-3");
    }

    [Fact]
    public async Task PollTransactionsAsync_RespectsMaxCountLimit()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var tx = CreateValidTransaction("tx-1");
        var json = JsonSerializer.Serialize(tx, jsonOptions);

        // Queue has many items, but we only request 2
        var popCallCount = 0;
        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                popCallCount++;
                return new SortedSetEntry($"tx-{popCallCount}", 0);
            });

        _mockDatabase.Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act - request only 2
        var result = await poller.PollTransactionsAsync("register-1", 2);

        // Assert - should only pop exactly 2 times
        result.Should().HaveCount(2);
        popCallCount.Should().Be(2);
    }

    #endregion

    #region GetUnverifiedCountAsync Tests

    [Fact]
    public async Task GetUnverifiedCountAsync_ReturnsQueueLength()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(42);

        // Act
        var result = await poller.GetUnverifiedCountAsync("register-1");

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task GetUnverifiedCountAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.GetUnverifiedCountAsync(null!));
    }

    [Fact]
    public async Task GetUnverifiedCountAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.GetUnverifiedCountAsync(""));
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingTransaction_ReturnsTrue()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await poller.ExistsAsync("register-1", "tx-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentTransaction_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await poller.ExistsAsync("register-1", "tx-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.ExistsAsync(null!, "tx-1"));
    }

    [Fact]
    public async Task ExistsAsync_WithNullTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.ExistsAsync("register-1", null!));
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.ExistsAsync("", "tx-1"));
    }

    [Fact]
    public async Task ExistsAsync_WithEmptyTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.ExistsAsync("register-1", ""));
    }

    #endregion

    #region RemoveTransactionAsync Tests

    [Fact]
    public async Task RemoveTransactionAsync_WithExistingTransaction_ReturnsTrue()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // SUT uses _database.CreateTransaction() and pipelines three removes (SEC-AUDIT 4.9):
        // SortedSetRemove queue, KeyDelete data, SortedSetRemove expiry. Mock the transaction
        // so queueRemove and dataRemove both succeed → method returns true.
        var mockTx = new Mock<ITransaction>();
        mockTx.Setup(t => t.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockTx.Setup(t => t.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockTx.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTx.Object);

        // Act
        var result = await poller.RemoveTransactionAsync("register-1", "tx-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithNonExistentTransaction_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.ListRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        _mockDatabase.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        _mockDatabase.Setup(x => x.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await poller.RemoveTransactionAsync("register-1", "tx-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.RemoveTransactionAsync(null!, "tx-1"));
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.RemoveTransactionAsync("", "tx-1"));
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithNullTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.RemoveTransactionAsync("register-1", null!));
    }

    [Fact]
    public async Task RemoveTransactionAsync_WithEmptyTransactionId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.RemoveTransactionAsync("register-1", ""));
    }

    #endregion

    #region GetStatsAsync Tests

    [Fact]
    public async Task GetStatsAsync_ReturnsStats()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(10);

        _mockDatabase.Setup(x => x.SortedSetRangeByRankWithScoresAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<Order>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<SortedSetEntry>());

        // Act
        var stats = await poller.GetStatsAsync("register-1");

        // Assert
        stats.RegisterId.Should().Be("register-1");
        stats.TotalTransactions.Should().Be(10);
    }

    [Fact]
    public async Task GetStatsAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.GetStatsAsync(null!));
    }

    [Fact]
    public async Task GetStatsAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => poller.GetStatsAsync(""));
    }

    [Fact]
    public async Task GetStatsAsync_WithSortedSetEntries_CalculatesOldestTimeAndAverageAge()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var now = DateTimeOffset.UtcNow;

        // Transactions that expire 30 min and 60 min from now
        var expiryScore1 = now.AddMinutes(30).ToUnixTimeSeconds();
        var expiryScore2 = now.AddMinutes(60).ToUnixTimeSeconds();

        _mockDatabase.Setup(x => x.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        // First call (0, 0) returns oldest entry only
        // Second call (0, -1) returns all entries
        var rankCallCount = 0;
        _mockDatabase.Setup(x => x.SortedSetRangeByRankWithScoresAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<Order>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey _, long start, long stop, Order _, CommandFlags _) =>
            {
                rankCallCount++;
                if (rankCallCount == 1) // oldest only (0, 0)
                {
                    return new[] { new SortedSetEntry("tx-1", expiryScore1) };
                }
                // all entries (0, -1)
                return new[]
                {
                    new SortedSetEntry("tx-1", expiryScore1),
                    new SortedSetEntry("tx-2", expiryScore2)
                };
            });

        // Act
        var stats = await poller.GetStatsAsync("register-1");

        // Assert
        stats.RegisterId.Should().Be("register-1");
        stats.TotalTransactions.Should().Be(2);
        stats.OldestTransactionTime.Should().NotBeNull();
        stats.AverageAgeMs.Should().BeGreaterThan(0);
    }

    #endregion

    #region ReturnTransactionsAsync Tests

    [Fact]
    public async Task ReturnTransactionsAsync_WithEmptyList_DoesNothing()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act - Should not throw
        await poller.ReturnTransactionsAsync("register-1", new List<Transaction>());

        // Assert - No database operations should have been performed
        _mockDatabase.Verify(x => x.CreateTransaction(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_WithNullList_DoesNothing()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act - Should not throw
        await poller.ReturnTransactionsAsync("register-1", null!);

        // Assert - No database operations should have been performed
        _mockDatabase.Verify(x => x.CreateTransaction(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_IncrementsRetryCount()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");
        transaction.RetryCount = 1;

        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var transactions = new List<Transaction> { transaction };

        // Act
        await poller.ReturnTransactionsAsync("register-1", transactions);

        // Assert
        transaction.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task ReturnTransactionsAsync_WithMultipleTransactions_IncrementsAllRetryCounts()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var tx1 = CreateValidTransaction("tx-1");
        tx1.RetryCount = 0;
        var tx2 = CreateValidTransaction("tx-2");
        tx2.RetryCount = 2;

        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var transactions = new List<Transaction> { tx1, tx2 };

        // Act
        await poller.ReturnTransactionsAsync("register-1", transactions);

        // Assert
        tx1.RetryCount.Should().Be(1);
        tx2.RetryCount.Should().Be(3);
    }

    #endregion

    #region CleanupExpiredAsync Tests

    [Fact]
    public async Task CleanupExpiredAsync_WithExpiredTransactions_RemovesThem()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        var expiredEntries = new[]
        {
            new SortedSetEntry("tx-1", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()),
            new SortedSetEntry("tx-2", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds())
        };

        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(expiredEntries.Select(e => e.Element).ToArray());

        // SUT now uses _database.CreateTransaction() per expired entry to remove atomically
        // (SEC-AUDIT 4.9). Mock the transaction handle.
        var mockCleanupTx = new Mock<ITransaction>();
        mockCleanupTx.Setup(t => t.SortedSetRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockCleanupTx.Setup(t => t.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        mockCleanupTx.Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockCleanupTx.Object);

        // Act
        var result = await poller.CleanupExpiredAsync("register-1");

        // Assert
        result.Should().Be(2);
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithNoExpiredTransactions_ReturnsZero()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        // Act
        var result = await poller.CleanupExpiredAsync("register-1");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CleanupExpiredAsync_WithNullRegisterId_ThrowsArgumentException()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => poller.CleanupExpiredAsync(null!));
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task SubmitTransactionAsync_WhenRedisThrows_ReturnsFalseAndHandlesGracefully()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert - should not throw, returns false
        result.Should().BeFalse();
    }

    [Fact]
    public async Task PollTransactionsAsync_WhenRedisThrows_ReturnsEmptyListAndHandlesGracefully()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetPopAsync(It.IsAny<RedisKey>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.PollTransactionsAsync("register-1", 10);

        // Assert - should not throw, returns empty
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnverifiedCountAsync_WhenRedisThrows_ReturnsZero()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.GetUnverifiedCountAsync("register-1");

        // Assert - should handle gracefully and return 0
        result.Should().Be(0);
    }

    [Fact]
    public async Task ExistsAsync_WhenRedisThrows_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.ExistsAsync("register-1", "tx-1");

        // Assert - should handle gracefully and return false
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveTransactionAsync_WhenRedisThrows_ReturnsFalse()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.ListRemoveAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.RemoveTransactionAsync("register-1", "tx-1");

        // Assert - should handle gracefully and return false
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredAsync_WhenRedisThrows_ReturnsZeroAndHandlesGracefully()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        _mockDatabase.Setup(x => x.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<Exclude>(),
            It.IsAny<Order>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var result = await poller.CleanupExpiredAsync("register-1");

        // Assert - should handle gracefully and return 0
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetStatsAsync_WhenRedisThrowsOnSortedSet_ReturnsPartialStats()
    {
        // Arrange
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);

        // SortedSetLength (queue) works for count
        _mockDatabase.Setup(x => x.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(5);

        // SortedSet throws for stats
        _mockDatabase.Setup(x => x.SortedSetRangeByRankWithScoresAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<Order>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Connection refused"));

        // Act
        var stats = await poller.GetStatsAsync("register-1");

        // Assert - should return partial stats without crashing
        stats.RegisterId.Should().Be("register-1");
        stats.TotalTransactions.Should().Be(5);
        stats.OldestTransactionTime.Should().BeNull();
        stats.AverageAgeMs.Should().Be(0);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task SubmitTransactionAsync_UsesConfiguredKeyPrefix()
    {
        // Arrange
        var customConfig = new TransactionPoolPollerConfiguration
        {
            KeyPrefix = "custom:prefix:",
            BatchSize = 50,
            TransactionTtl = TimeSpan.FromHours(1),
            MaxRetries = 3
        };
        var customOptions = Options.Create(customConfig);
        var poller = new TransactionPoolPoller(_mockRedis.Object, customOptions, _mockLogger.Object);
        var transaction = CreateValidTransaction("tx-1");

        RedisKey capturedExistsKey = default;
        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, _) => capturedExistsKey = key)
            .ReturnsAsync(false);

        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>()))
            .Returns(mockTransaction.Object);

        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await poller.SubmitTransactionAsync("register-1", transaction);

        // Assert - key should use the custom prefix
        capturedExistsKey.ToString().Should().StartWith("custom:prefix:register-1:data:");
    }

    [Fact]
    public void Configuration_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new TransactionPoolPollerConfiguration();

        // Assert
        config.PollingInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        config.BatchSize.Should().Be(50);
        config.KeyPrefix.Should().Be("sorcha:validator:unverified:");
        config.BlockingTimeout.Should().Be(TimeSpan.FromSeconds(5));
        config.UseBlockingPop.Should().BeTrue();
        config.MaxRetries.Should().Be(3);
        config.RetryDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        config.TransactionTtl.Should().Be(TimeSpan.FromHours(1));
        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Configuration_SectionName_IsCorrect()
    {
        // Assert
        TransactionPoolPollerConfiguration.SectionName.Should().Be("TransactionPoolPoller");
    }

    #endregion

    #region Helper Methods

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
            Signatures = new List<RegisterSignature>
            {
                new RegisterSignature
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

    #region ReturnTransactionsAsync retry-bound (#787)

    [Fact]
    public async Task ReturnTransactionsAsync_ExceedsMaxTransactionRetries_EvictsAndLogsCritical()
    {
        // Arrange — a transaction already at the bound; the increment tips it over.
        _config.MaxTransactionRetries = 2;
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var stuck = CreateValidTransaction("tx-stuck");
        stuck.RetryCount = 2; // ++ -> 3 > 2

        // Act
        await poller.ReturnTransactionsAsync("register-1", new[] { stuck });

        // Assert — evicted (never re-submitted) and flagged for operators.
        _mockDatabase.Verify(x => x.CreateTransaction(It.IsAny<object>()), Times.Never,
            "an over-limit transaction must be evicted, not re-submitted to the pool");
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "eviction must raise a LogCritical operator signal");
    }

    [Fact]
    public async Task ReturnTransactionsAsync_UnderMaxTransactionRetries_ResubmitsNotEvicted()
    {
        // Arrange — comfortably under the bound; must be returned to the pool as before.
        _config.MaxTransactionRetries = 50;
        var poller = new TransactionPoolPoller(_mockRedis.Object, _options, _mockLogger.Object);
        var mockTransaction = new Mock<ITransaction>();
        _mockDatabase.Setup(x => x.CreateTransaction(It.IsAny<object>())).Returns(mockTransaction.Object);
        _mockDatabase.Setup(x => x.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(false);
        mockTransaction.Setup(x => x.ExecuteAsync(It.IsAny<CommandFlags>())).ReturnsAsync(true);
        var ok = CreateValidTransaction("tx-ok"); // RetryCount 0 -> 1

        // Act
        await poller.ReturnTransactionsAsync("register-1", new[] { ok });

        // Assert — re-submitted, no eviction alert.
        _mockDatabase.Verify(x => x.CreateTransaction(It.IsAny<object>()), Times.AtLeastOnce,
            "an under-limit transaction must be re-submitted to the pool");
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion
}
