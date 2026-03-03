// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

public class NotificationDeliveryServiceTests
{
    private readonly Mock<IWalletRepository> _mockWalletRepository;
    private readonly Mock<INotificationRateLimiter> _mockRateLimiter;
    private readonly Mock<INotificationPreferenceProvider> _mockPreferenceProvider;
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<ISubscriber> _mockSubscriber;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<NotificationDeliveryService>> _mockLogger;
    private readonly NotificationDeliveryService _service;

    private const string TestAddress = "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq";
    private const string TestTxId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string TestRegisterId = "reg-001";
    private const string TestUserId = "user-001";
    private const string TestTenantId = "tenant-001";
    private const string TestBlueprintId = "bp-001";
    private const string TestInstanceId = "inst-001";
    private const string TestSenderAddress = "bc1qsender0000000000000000000000000000000";

    private static readonly DateTimeOffset TestTimestamp = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public NotificationDeliveryServiceTests()
    {
        _mockWalletRepository = new Mock<IWalletRepository>();
        _mockRateLimiter = new Mock<INotificationRateLimiter>();
        _mockPreferenceProvider = new Mock<INotificationPreferenceProvider>();
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<NotificationDeliveryService>>();

        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_mockSubscriber.Object);
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);

        _service = new NotificationDeliveryService(
            _mockWalletRepository.Object,
            _mockRateLimiter.Object,
            _mockPreferenceProvider.Object,
            new NotificationMetrics(new TestMeterFactory()),
            _mockRedis.Object,
            _mockLogger.Object);
    }

    // ---------------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------------

    private static WalletEntity CreateTestWallet()
    {
        return new WalletEntity
        {
            Address = TestAddress,
            Owner = TestUserId,
            Tenant = TestTenantId,
            Name = "Test Wallet",
            EncryptedPrivateKey = "encrypted-key-data",
            EncryptionKeyId = "key-001",
            Algorithm = "ED25519"
        };
    }

    private void SetupWalletFound(WalletEntity? wallet = null)
    {
        _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(
                TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet ?? CreateTestWallet());
    }

    private void SetupWalletNotFound()
    {
        _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(
                TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);
    }

    private void SetupPreferences(NotificationPreferences prefs)
    {
        _mockPreferenceProvider
            .Setup(p => p.GetPreferencesAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);
    }

    private void SetupRateLimiter(bool allowed)
    {
        _mockRateLimiter
            .Setup(r => r.TryAcquireAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);
    }

    private Task<NotificationDeliveryResult> CallDeliverAsync()
    {
        return _service.DeliverAsync(
            TestAddress,
            TestTxId,
            TestRegisterId,
            docketNumber: 42,
            TestBlueprintId,
            TestInstanceId,
            actionId: 1,
            nextActionId: 2,
            TestSenderAddress,
            TestTimestamp,
            isRecovery: false);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Real-time delivery
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletFoundRealTimePrefsRateLimitOk_PublishesToRedisPubSubAndReturnsDeliveredRealTime()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Digest queue routing
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletFoundDigestPrefs_QueuesInRedisSortedSetAndReturnsQueuedForDigest()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.QueuedForDigest);
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.Is<RedisKey>(k => k.ToString() == $"wallet:digest:{TestUserId}"),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()),
            Times.Once);

        // Verify user is added to the active-users set for efficient digest lookup
        _mockDatabase.Verify(
            db => db.SetAddAsync(
                (RedisKey)"wallet:digest:active-users",
                (RedisValue)TestUserId,
                It.IsAny<CommandFlags>()),
            Times.Once);

        _mockRateLimiter.Verify(
            r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Digest routing should skip rate limiter entirely");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Rate-limited overflow to digest
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletFoundRealTimePrefsRateLimited_QueuesToDigestAndReturnsRateLimited()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: false);

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.RateLimited);
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.Is<RedisKey>(k => k.ToString() == $"wallet:digest:{TestUserId}"),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()),
            Times.Once);

        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Rate-limited notifications should not be published to pub/sub");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — No user found
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletNotFound_ReturnsNoUserFound()
    {
        // Arrange
        SetupWalletNotFound();

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockPreferenceProvider.Verify(
            p => p.GetPreferencesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not check preferences when no wallet exists");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Notifications disabled
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_NotificationsDisabled_ReturnsNoUserFound()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { NotificationsEnabled = false });

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockRateLimiter.Verify(
            r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Should not check rate limit when notifications are disabled");
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Email preference fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WantsEmail_DeliversInAppWithoutException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { WantsEmail = true, IsRealTime = true });
        SetupRateLimiter(allowed: true);

        // Act
        var act = () => CallDeliverAsync();

        // Assert — should not throw; email preference logged but delivery proceeds in-app
        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once,
            "In-app delivery should proceed even when email preference is set");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Push preference fallback
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WantsPush_DeliversInAppWithoutException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { WantsPush = true, IsRealTime = true });
        SetupRateLimiter(allowed: true);

        // Act
        var act = () => CallDeliverAsync();

        // Assert — should not throw; push preference logged but delivery proceeds in-app
        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once,
            "In-app delivery should proceed even when push preference is set");
    }
}
