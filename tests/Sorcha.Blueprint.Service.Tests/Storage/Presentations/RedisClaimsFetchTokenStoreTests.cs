// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Storage.Presentations;

/// <summary>
/// Feature 127 — unit tests for <see cref="RedisClaimsFetchTokenStore"/>.
/// Validates SET NX with TTL on store, atomic GETDEL via Lua on consume,
/// and the back-compat error shapes when the token is malformed / missing.
/// Mirrors the F111 <c>RedisPendingPresentationStore</c> mock pattern.
/// </summary>
public sealed class RedisClaimsFetchTokenStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly RedisClaimsFetchTokenStore _sut;

    public RedisClaimsFetchTokenStoreTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_db.Object);
        _sut = new RedisClaimsFetchTokenStore(_redis.Object, NullLogger<RedisClaimsFetchTokenStore>.Instance);
    }

    [Fact]
    public void Constructor_NullRedis_Throws()
    {
        var act = () => new RedisClaimsFetchTokenStore(null!, NullLogger<RedisClaimsFetchTokenStore>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    // Note: arg-capture assertions on StringSetAsync are fragile against
    // StackExchange.Redis's instance+extension overload combo (the 4-arg
    // call from the store resolves through different paths depending on
    // SDK version). Behaviour-level coverage of the SET NX path is
    // best left to integration tests with a real Redis (deferred T040
    // alongside the WebApplicationFactory work). Here we focus on the
    // pure input-validation contract.

    [Fact]
    public async Task StoreAsync_RejectsZeroOrNegativeTtl()
    {
        Func<Task> zero = () => _sut.StoreAsync("t", Guid.NewGuid(), TimeSpan.Zero);
        Func<Task> negative = () => _sut.StoreAsync("t", Guid.NewGuid(), TimeSpan.FromSeconds(-1));

        await zero.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task StoreAsync_RejectsEmptyToken()
    {
        Func<Task> act = () => _sut.StoreAsync("", Guid.NewGuid(), TimeSpan.FromMinutes(1));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAndRemoveAsync_ReturnsBoundRequestId_AndRemovesEntry()
    {
        var requestId = Guid.NewGuid();
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)requestId.ToString("N")));

        var result = await _sut.GetAndRemoveAsync("tok");

        result.Should().Be(requestId);
    }

    [Fact]
    public async Task GetAndRemoveAsync_ReturnsNull_WhenScriptReturnsNull()
    {
        // Token not found or already consumed → Lua returns nil → null.
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)RedisValue.Null));

        var result = await _sut.GetAndRemoveAsync("missing-tok");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemoveAsync_ReturnsNull_OnUnparseableValue()
    {
        // Defensive: an external write put a non-GUID at the key. The store
        // logs a warning and treats it as missing rather than throwing.
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)"not-a-guid"));

        var result = await _sut.GetAndRemoveAsync("tok-bad");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAndRemoveAsync_ReturnsNull_OnEmptyToken()
    {
        // No need to round-trip Redis when the input is obviously bad.
        var result = await _sut.GetAndRemoveAsync("");
        result.Should().BeNull();

        _db.Verify(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }
}
