// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Moq;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Xunit;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.Redis.Tests;

public class RedisCacheStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IServer> _mockServer;
    private readonly RedisCacheStore _store;

    public RedisCacheStoreTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockServer = new Mock<IServer>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
        _mockRedis.Setup(r => r.GetServers())
            .Returns([_mockServer.Object]);

        _store = new RedisCacheStore(_mockRedis.Object, "test:", TimeSpan.FromMinutes(5));
    }

    // ── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new RedisCacheStore(null!, "prefix:");

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("connection");
    }

    [Fact]
    public void Constructor_DefaultParameters_SetsExpectedDefaults()
    {
        var store = new RedisCacheStore(_mockRedis.Object);

        store.Should().NotBeNull();
    }

    // ── GetAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_KeyExists_ReturnsDeserializedValue()
    {
        var expected = new TestDto { Name = "Alice", Age = 30 };
        var json = JsonSerializer.Serialize(expected);
        _mockDatabase.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == "test:user:1"), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        var result = await _store.GetAsync<TestDto>("user:1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.Age.Should().Be(30);
    }

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsNull()
    {
        _mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _store.GetAsync<TestDto>("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_BrokenCircuit_ReturnsDefault()
    {
        _mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new BrokenCircuitException());

        var result = await _store.GetAsync<TestDto>("key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_Timeout_ReturnsDefault()
    {
        _mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException("timed out"));

        var result = await _store.GetAsync<TestDto>("key");

        result.Should().BeNull();
    }

    // ── SetAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_WithValue_StoresSerializedJson()
    {
        var dto = new TestDto { Name = "Bob", Age = 25 };

        await _store.SetAsync("user:2", dto, TimeSpan.FromMinutes(10));

        var invocation = _mockDatabase.Invocations
            .FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        invocation.Should().NotBeNull("StringSetAsync should have been called");

        var storedKey = (RedisKey)invocation!.Arguments[0];
        var storedValue = (RedisValue)invocation.Arguments[1];
        storedKey.ToString().Should().Be("test:user:2");
        storedValue.ToString().Should().Contain("Bob");
    }

    [Fact]
    public async Task SetAsync_NoExplicitExpiration_UsesDefault()
    {
        await _store.SetAsync("key", "value");

        var invocation = _mockDatabase.Invocations
            .FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        invocation.Should().NotBeNull("StringSetAsync should have been called");

        // The TTL argument may be a TimeSpan? or StackExchange.Redis.Expiration depending on version
        var ttlArg = invocation!.Arguments[2];
        ttlArg.Should().NotBeNull("a TTL should be provided");
        ttlArg!.ToString().Should().Contain("300", "default expiration is 5 minutes (300 seconds)");
    }

    [Fact]
    public async Task SetAsync_BrokenCircuit_DoesNotThrow()
    {
        _mockDatabase.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new BrokenCircuitException());

        var act = () => _store.SetAsync("key", "value");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_Timeout_DoesNotThrow()
    {
        _mockDatabase.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException());

        var act = () => _store.SetAsync("key", "value");

        await act.Should().NotThrowAsync();
    }

    // ── RemoveAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_KeyExists_ReturnsTrueAndDeletesKey()
    {
        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.Is<RedisKey>(k => k == "test:item"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _store.RemoveAsync("item");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveAsync_KeyNotFound_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var result = await _store.RemoveAsync("missing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_BrokenCircuit_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new BrokenCircuitException());

        var result = await _store.RemoveAsync("key");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_Timeout_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException());

        var result = await _store.RemoveAsync("key");

        result.Should().BeFalse();
    }

    // ── ExistsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ExistsAsync_KeyExists_ReturnsTrue()
    {
        _mockDatabase.Setup(d => d.KeyExistsAsync(It.Is<RedisKey>(k => k == "test:exists"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await _store.ExistsAsync("exists");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_KeyNotFound_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var result = await _store.ExistsAsync("nope");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_BrokenCircuit_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new BrokenCircuitException());

        var result = await _store.ExistsAsync("key");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_Timeout_ReturnsFalse()
    {
        _mockDatabase.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException());

        var result = await _store.ExistsAsync("key");

        result.Should().BeFalse();
    }

    // ── GetOrSetAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetOrSetAsync_CacheHit_ReturnsCachedValueWithoutCallingFactory()
    {
        var cached = new TestDto { Name = "Cached", Age = 99 };
        var json = JsonSerializer.Serialize(cached);
        _mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        var factoryCalled = false;
        var result = await _store.GetOrSetAsync<TestDto>("key", _ =>
        {
            factoryCalled = true;
            return Task.FromResult(new TestDto { Name = "Fresh", Age = 1 });
        });

        result.Should().NotBeNull();
        result.Name.Should().Be("Cached");
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_CallsFactoryAndStoresResult()
    {
        _mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _store.GetOrSetAsync<TestDto>("key", _ =>
            Task.FromResult(new TestDto { Name = "Fresh", Age = 1 }));

        result.Should().NotBeNull();
        result.Name.Should().Be("Fresh");

        var setInvocation = _mockDatabase.Invocations
            .FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        setInvocation.Should().NotBeNull("SetAsync should store the factory result");

        var storedValue = (RedisValue)setInvocation!.Arguments[1];
        storedValue.ToString().Should().Contain("Fresh");
    }

    // ── RemoveByPatternAsync ─────────────────────────────────────────

    [Fact]
    public async Task RemoveByPatternAsync_MatchingKeys_RemovesAndReturnsCount()
    {
        var keys = new RedisKey[] { "test:user:1", "test:user:2" };
        _mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(keys.AsEnumerable());

        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        var result = await _store.RemoveByPatternAsync("user:*");

        result.Should().Be(2);
    }

    [Fact]
    public async Task RemoveByPatternAsync_NoServer_ReturnsZero()
    {
        _mockRedis.Setup(r => r.GetServers()).Returns([]);

        var store = new RedisCacheStore(_mockRedis.Object, "test:");

        var result = await store.RemoveByPatternAsync("user:*");

        result.Should().Be(0);
    }

    // ── IncrementAsync ───────────────────────────────────────────────

    [Fact]
    public async Task IncrementAsync_Default_IncrementsBy1()
    {
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.Is<RedisKey>(k => k == "test:counter"), 1, It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        var result = await _store.IncrementAsync("counter");

        result.Should().Be(1);
    }

    [Fact]
    public async Task IncrementAsync_WithDelta_IncrementsCorrectly()
    {
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.Is<RedisKey>(k => k == "test:counter"), 5, It.IsAny<CommandFlags>()))
            .ReturnsAsync(10);

        var result = await _store.IncrementAsync("counter", delta: 5);

        result.Should().Be(10);
    }

    [Fact]
    public async Task IncrementAsync_FirstIncrement_SetsExpiration()
    {
        var expiration = TimeSpan.FromMinutes(1);
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        await _store.IncrementAsync("counter", expiration: expiration);

        _mockDatabase.Verify(d => d.KeyExpireAsync(
            It.Is<RedisKey>(k => k == "test:counter"),
            It.IsAny<TimeSpan?>(),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task IncrementAsync_SubsequentIncrement_DoesNotResetExpiration()
    {
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1, It.IsAny<CommandFlags>()))
            .ReturnsAsync(10);

        await _store.IncrementAsync("counter", expiration: TimeSpan.FromMinutes(1));

        _mockDatabase.Verify(d => d.KeyExpireAsync(
            It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task IncrementAsync_BrokenCircuit_ReturnsDelta()
    {
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new BrokenCircuitException());

        var result = await _store.IncrementAsync("counter", delta: 3);

        result.Should().Be(3);
    }

    [Fact]
    public async Task IncrementAsync_Timeout_ReturnsDelta()
    {
        _mockDatabase.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException());

        var result = await _store.IncrementAsync("counter", delta: 3);

        result.Should().Be(3);
    }

    // ── GetStatisticsAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetStatisticsAsync_NoRequests_ReturnsZeroStatistics()
    {
        _mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(Enumerable.Empty<RedisKey>());

        var stats = await _store.GetStatisticsAsync();

        stats.TotalRequests.Should().Be(0);
        stats.Hits.Should().Be(0);
        stats.Misses.Should().Be(0);
        stats.AverageLatencyMs.Should().Be(0);
        stats.P99LatencyMs.Should().Be(0);
        stats.HitRate.Should().Be(0);
    }

    [Fact]
    public async Task GetStatisticsAsync_AfterHitsAndMisses_TracksCorrectly()
    {
        var json = JsonSerializer.Serialize(new TestDto { Name = "A", Age = 1 });
        _mockDatabase.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == "test:hit"), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);
        _mockDatabase.Setup(d => d.StringGetAsync(It.Is<RedisKey>(k => k == "test:miss"), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        _mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(Enumerable.Empty<RedisKey>());

        await _store.GetAsync<TestDto>("hit");
        await _store.GetAsync<TestDto>("miss");

        var stats = await _store.GetStatisticsAsync();

        stats.TotalRequests.Should().Be(2);
        stats.Hits.Should().Be(1);
        stats.Misses.Should().Be(1);
        stats.HitRate.Should().Be(0.5);
    }

    [Fact]
    public async Task GetStatisticsAsync_AfterRemove_TracksEvictions()
    {
        _mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _mockServer.Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(Enumerable.Empty<RedisKey>());

        await _store.RemoveAsync("key");

        var stats = await _store.GetStatisticsAsync();

        stats.EvictionCount.Should().Be(1);
    }

    // ── DisposeAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ExternalConnection_DoesNotDisposeConnection()
    {
        var store = new RedisCacheStore(_mockRedis.Object, "test:");

        await store.DisposeAsync();

        _mockRedis.Verify(r => r.Dispose(), Times.Never);
    }

    // ── Test DTO ─────────────────────────────────────────────────────

    private sealed class TestDto
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }
}
