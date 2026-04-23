// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Tests.Storage.Presentations;

/// <summary>
/// T024 — unit tests for RedisPresentationRateLimiter. Validates per-wallet-per-register
/// scoping, threshold enforcement, and that TTL is applied only on the first INCR.
/// </summary>
public class RedisPresentationRateLimiterTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis = new();
    private readonly Mock<IDatabase> _mockDb = new();

    public RedisPresentationRateLimiterTests()
    {
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDb.Object);
    }

    private RedisPresentationRateLimiter Make(int threshold = 10, int windowSeconds = 600)
    {
        var opts = Options.Create(new PresentationLifecycleOptions
        {
            RateLimit = new PresentationLifecycleOptions.RateLimitOptions
            {
                Threshold = threshold,
                WindowSeconds = windowSeconds
            }
        });
        return new RedisPresentationRateLimiter(
            _mockRedis.Object,
            opts,
            new Mock<ILogger<RedisPresentationRateLimiter>>().Object);
    }

    [Fact]
    public async Task CheckAsync_FirstAttempt_Allowed_AndAppliesTtl()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        TimeSpan? capturedTtl = null;
        _mockDb.Setup(d => d.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>((_, ttl, _, _) =>
                capturedTtl = ttl)
            .ReturnsAsync(true);

        var result = await Make(threshold: 10, windowSeconds: 600)
            .CheckAsync("ws11qcitizen", "reg-1");

        result.Allowed.Should().BeTrue();
        result.CurrentCount.Should().Be(1);
        result.Threshold.Should().Be(10);
        result.RetryAfter.Should().BeNull();
        capturedTtl.Should().Be(TimeSpan.FromSeconds(600));
    }

    [Fact]
    public async Task CheckAsync_BelowThreshold_Allowed_WithoutExpireReset()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(5L);

        var result = await Make(threshold: 10).CheckAsync("w", "r");

        result.Allowed.Should().BeTrue();
        result.CurrentCount.Should().Be(5);

        _mockDb.Verify(d => d.KeyExpireAsync(
            It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
            It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task CheckAsync_AtThreshold_Allowed()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(10L);

        var result = await Make(threshold: 10).CheckAsync("w", "r");

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_AboveThreshold_Rejected_WithRetryAfter()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(11L);

        _mockDb.Setup(d => d.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(TimeSpan.FromSeconds(123));

        var result = await Make(threshold: 10, windowSeconds: 600).CheckAsync("w", "r");

        result.Allowed.Should().BeFalse();
        result.CurrentCount.Should().Be(11);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(123));
    }

    [Fact]
    public async Task CheckAsync_PerWalletPerRegister_KeyScoping()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(1L);

        var keys = new List<string>();
        _mockDb.Setup(d => d.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>((k, _, _, _) =>
                keys.Add(k.ToString()))
            .ReturnsAsync(true);

        var limiter = Make();
        await limiter.CheckAsync("wallet-alice", "reg-a");
        await limiter.CheckAsync("wallet-alice", "reg-b");
        await limiter.CheckAsync("wallet-bob", "reg-a");

        keys.Should().BeEquivalentTo(
            "sorcha:presentation:ratelimit:wallet-alice:reg-a",
            "sorcha:presentation:ratelimit:wallet-alice:reg-b",
            "sorcha:presentation:ratelimit:wallet-bob:reg-a");
    }

    [Fact]
    public async Task CheckAsync_RejectedWithNoTtl_FallsBackToConfiguredWindow()
    {
        _mockDb.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), 1L, It.IsAny<CommandFlags>()))
            .ReturnsAsync(11L);
        _mockDb.Setup(d => d.KeyTimeToLiveAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((TimeSpan?)null);

        var result = await Make(threshold: 10, windowSeconds: 300).CheckAsync("w", "r");

        result.Allowed.Should().BeFalse();
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(300));
    }
}
