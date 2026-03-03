// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Service.Services.Implementation;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

public class NotificationRateLimiterTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<NotificationRateLimiter>> _mockLogger;

    private const string TestUserId = "user-001";
    private const string AnotherUserId = "user-002";
    private const string KeyPrefix = "wallet:ratelimit:";

    public NotificationRateLimiterTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<NotificationRateLimiter>>();

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(_mockDatabase.Object);
    }

    private NotificationRateLimiter CreateService(int? maxPerMinute = null)
    {
        var configData = new Dictionary<string, string?>();
        if (maxPerMinute.HasValue)
        {
            configData["Notifications:RealTimeRateLimitPerMinute"] = maxPerMinute.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new NotificationRateLimiter(
            _mockRedis.Object,
            configuration,
            _mockLogger.Object);
    }

    // ---------------------------------------------------------------------------
    // TryAcquireAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireAsync_UnderLimit_ReturnsTrueAndCounterIsOne()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(1);

        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}",
                It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert
        result.Should().BeTrue();
        _mockDatabase.Verify(
            db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_AtLimit_ReturnsFalse()
    {
        // Arrange — count exceeds default max of 10
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(11);

        var service = CreateService();

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_ExactlyAtMaxPerMinute_ReturnsTrue()
    {
        // Arrange — count equals default max of 10 (still allowed)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(10);

        var service = CreateService();

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_FirstIncrement_SetsTtl()
    {
        // Arrange — count is 1 (first increment in a new window)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(1);

        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}",
                It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        await service.TryAcquireAsync(TestUserId);

        // Assert — KeyExpireAsync called with 60-second window
        _mockDatabase.Verify(
            db => db.KeyExpireAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}",
                TimeSpan.FromSeconds(60),
                ExpireWhen.Always,
                CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_SubsequentIncrement_DoesNotSetTtl()
    {
        // Arrange — count is 5 (not the first increment)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(5);

        var service = CreateService();

        // Act
        await service.TryAcquireAsync(TestUserId);

        // Assert — KeyExpireAsync should NOT be called
        _mockDatabase.Verify(
            db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentUsers_UseIsolatedKeys()
    {
        // Arrange — user-001 at count 10 (at limit), user-002 at count 1 (fresh)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(10);

        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{AnotherUserId}", 1, CommandFlags.None))
            .ReturnsAsync(1);

        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                (RedisKey)$"{KeyPrefix}{AnotherUserId}",
                It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var resultUser1 = await service.TryAcquireAsync(TestUserId);
        var resultUser2 = await service.TryAcquireAsync(AnotherUserId);

        // Assert — user-001 at limit (allowed), user-002 well under limit
        resultUser1.Should().BeTrue();
        resultUser2.Should().BeTrue();

        // Verify each user's key was incremented independently
        _mockDatabase.Verify(
            db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None),
            Times.Once);
        _mockDatabase.Verify(
            db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{AnotherUserId}", 1, CommandFlags.None),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_CustomRateLimit_RespectsConfiguredValue()
    {
        // Arrange — custom limit of 5, count at 6 (exceeds custom limit)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(6);

        var service = CreateService(maxPerMinute: 5);

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert — blocked at custom limit of 5
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_CustomRateLimit_AllowsUpToConfiguredValue()
    {
        // Arrange — custom limit of 5, count at 5 (exactly at limit)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(5);

        var service = CreateService(maxPerMinute: 5);

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert — still allowed at exactly the limit
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_DefaultRateLimit_AllowsTenPerMinute()
    {
        // Arrange — no custom config, count at 10 (default limit)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(10);

        var service = CreateService();

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert — allowed at default limit of 10
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_DefaultRateLimit_BlocksEleventhPerMinute()
    {
        // Arrange — no custom config, count at 11 (exceeds default limit)
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", 1, CommandFlags.None))
            .ReturnsAsync(11);

        var service = CreateService();

        // Act
        var result = await service.TryAcquireAsync(TestUserId);

        // Assert — blocked beyond default limit of 10
        result.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // GetCurrentCountAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetCurrentCountAsync_NewUser_ReturnsZero()
    {
        // Arrange — no key exists, StringGetAsync returns RedisValue.Null
        _mockDatabase
            .Setup(db => db.StringGetAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", CommandFlags.None))
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService();

        // Act
        var count = await service.GetCurrentCountAsync(TestUserId);

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentCountAsync_ActiveUser_ReturnsCurrentCount()
    {
        // Arrange — user has 7 notifications in the current window
        _mockDatabase
            .Setup(db => db.StringGetAsync(
                (RedisKey)$"{KeyPrefix}{TestUserId}", CommandFlags.None))
            .ReturnsAsync(new RedisValue("7"));

        var service = CreateService();

        // Act
        var count = await service.GetCurrentCountAsync(TestUserId);

        // Assert
        count.Should().Be(7);
    }
}
