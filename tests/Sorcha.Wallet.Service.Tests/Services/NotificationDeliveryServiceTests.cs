// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

extern alias ServiceClients;

using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceClients::Sorcha.ServiceClients.Models;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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

    private Task<NotificationDeliveryResult> CallDeliverAsync(
        bool isRecovery = false,
        CancellationToken cancellationToken = default)
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
            isRecovery: isRecovery,
            cancellationToken: cancellationToken);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Real-time delivery (happy path)
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
    // DeliverAsync — Real-time JSON payload verification
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RealTimeDelivery_PublishesCorrectInboundActionEventJson()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        // Act
        await CallDeliverAsync();

        // Assert — verify the JSON payload contains the correct event fields
        capturedJson.Should().NotBeNull();
        var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(capturedJson!, JsonOptions);
        actionEvent.Should().NotBeNull();
        actionEvent!.WalletAddress.Should().Be(TestAddress);
        actionEvent.UserId.Should().Be(TestUserId);
        actionEvent.TenantId.Should().Be(TestTenantId);
        actionEvent.TransactionId.Should().Be(TestTxId);
        actionEvent.RegisterId.Should().Be(TestRegisterId);
        actionEvent.DocketNumber.Should().Be(42);
        actionEvent.BlueprintId.Should().Be(TestBlueprintId);
        actionEvent.InstanceId.Should().Be(TestInstanceId);
        actionEvent.ActionId.Should().Be(1u);
        actionEvent.NextActionId.Should().Be(2u);
        actionEvent.SenderAddress.Should().Be(TestSenderAddress);
        actionEvent.Timestamp.Should().Be(TestTimestamp);
        actionEvent.IsRecoveryEvent.Should().BeFalse();
    }

    [Fact]
    public async Task DeliverAsync_RecoveryEvent_SetsIsRecoveryEventTrue()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        // Act
        await CallDeliverAsync(isRecovery: true);

        // Assert
        capturedJson.Should().NotBeNull();
        var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(capturedJson!, JsonOptions);
        actionEvent!.IsRecoveryEvent.Should().BeTrue();
    }

    [Fact]
    public async Task DeliverAsync_RealTimeDelivery_DoesNotWriteToSortedSet()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        // Act
        await CallDeliverAsync();

        // Assert — no digest queue interaction
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Real-time delivery should not queue to digest sorted set");
        _mockDatabase.Verify(
            db => db.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Real-time delivery should not add user to active-users set");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Digest queue routing (happy path)
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

    [Fact]
    public async Task DeliverAsync_DigestPrefs_UsesTimestampAsScore()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        double capturedScore = 0;
        _mockDatabase
            .Setup(db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, double, SortedSetWhen, CommandFlags>(
                (_, _, score, _, _) => capturedScore = score)
            .ReturnsAsync(true);

        // Act
        await CallDeliverAsync();

        // Assert — score should be the TestTimestamp as Unix milliseconds
        var expectedScore = TestTimestamp.ToUnixTimeMilliseconds();
        capturedScore.Should().Be(expectedScore);
    }

    [Fact]
    public async Task DeliverAsync_DigestPrefs_DoesNotPublishToPubSub()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        // Act
        await CallDeliverAsync();

        // Assert
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Digest routing should not publish to pub/sub");
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

    [Fact]
    public async Task DeliverAsync_RateLimited_AddsUserToActiveUsersSet()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: false);

        // Act
        await CallDeliverAsync();

        // Assert — rate-limited overflow goes to digest, which requires active-users tracking
        _mockDatabase.Verify(
            db => db.SetAddAsync(
                (RedisKey)"wallet:digest:active-users",
                (RedisValue)TestUserId,
                It.IsAny<CommandFlags>()),
            Times.Once);
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

    [Fact]
    public async Task DeliverAsync_WalletNotFound_DoesNotInteractWithRedis()
    {
        // Arrange
        SetupWalletNotFound();

        // Act
        await CallDeliverAsync();

        // Assert — no Redis operations at all
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
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

    [Fact]
    public async Task DeliverAsync_NotificationsDisabledButRealTime_StillReturnsNoUserFound()
    {
        // Arrange — notifications disabled overrides IsRealTime
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences
        {
            NotificationsEnabled = false,
            IsRealTime = true
        });

        // Act
        var result = await CallDeliverAsync();

        // Assert
        result.Should().Be(NotificationDeliveryResult.NoUserFound);
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

    [Fact]
    public async Task DeliverAsync_WantsEmailAndPush_DeliversInAppWithoutException()
    {
        // Arrange — both unavailable transports configured
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences
        {
            WantsEmail = true,
            WantsPush = true,
            IsRealTime = true
        });
        SetupRateLimiter(allowed: true);

        // Act
        var result = await CallDeliverAsync();

        // Assert — still delivers real-time in-app
        result.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == "wallet:notifications"),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Redis pub/sub failure
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RedisPubSubThrows_PropagatesException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, "Connection lost"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert — Redis failure propagates (caller is responsible for retry/error handling)
        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Redis sorted set failure
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RedisSortedSetAddThrows_PropagatesException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        _mockDatabase
            .Setup(db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, "Connection lost"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert — Redis failure propagates
        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task DeliverAsync_RedisSetAddActiveUsersThrows_PropagatesException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        _mockDatabase
            .Setup(db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.SetAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, "Connection lost"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert — Redis failure propagates
        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Wallet repository failure
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletRepositoryThrows_PropagatesException()
    {
        // Arrange
        _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(
                TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database connection failed");
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Preference provider failure
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_PreferenceProviderThrows_PropagatesException()
    {
        // Arrange
        SetupWalletFound();
        _mockPreferenceProvider
            .Setup(p => p.GetPreferencesAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant service unavailable"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Rate limiter integration
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RateLimiterThrows_PropagatesException()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        _mockRateLimiter
            .Setup(r => r.TryAcquireAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, "Connection lost"));

        // Act
        var act = () => CallDeliverAsync();

        // Assert
        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task DeliverAsync_DigestPrefs_DoesNotCallRateLimiter()
    {
        // Arrange — digest preference bypasses rate limiter entirely
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        // Act
        await CallDeliverAsync();

        // Assert
        _mockRateLimiter.Verify(
            r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Null/optional fields
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_NullBlueprintAndInstanceAndSender_DeliversSuccessfully()
    {
        // Arrange
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        // Act
        var result = await _service.DeliverAsync(
            TestAddress,
            TestTxId,
            TestRegisterId,
            docketNumber: 42,
            blueprintId: null,
            instanceId: null,
            actionId: 1,
            nextActionId: 2,
            senderAddress: null,
            TestTimestamp,
            isRecovery: false);

        // Assert
        result.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        capturedJson.Should().NotBeNull();
        var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(capturedJson!, JsonOptions);
        actionEvent!.BlueprintId.Should().BeNull();
        actionEvent.InstanceId.Should().BeNull();
        actionEvent.SenderAddress.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Wallet with null tenant
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletWithNullTenant_SetsNullTenantIdInEvent()
    {
        // Arrange
        var walletWithNoTenant = new WalletEntity
        {
            Address = TestAddress,
            Owner = TestUserId,
            Tenant = null,
            Name = "No Tenant Wallet",
            EncryptedPrivateKey = "encrypted-key-data",
            EncryptionKeyId = "key-001",
            Algorithm = "ED25519"
        };

        SetupWalletFound(walletWithNoTenant);
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        // Act
        await CallDeliverAsync();

        // Assert
        capturedJson.Should().NotBeNull();
        var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(capturedJson!, JsonOptions);
        actionEvent!.TenantId.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // DeliverAsync — Cancellation token propagation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_CancellationTokenPassedToRepository()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(
                TestAddress, false, false, false, token))
            .ReturnsAsync(CreateTestWallet())
            .Verifiable();

        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);

        // Act
        await CallDeliverAsync(cancellationToken: token);

        // Assert
        _mockWalletRepository.Verify(
            r => r.GetByAddressAsync(TestAddress, false, false, false, token),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_CancellationTokenPassedToPreferenceProvider()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        SetupWalletFound();
        _mockPreferenceProvider
            .Setup(p => p.GetPreferencesAsync(TestUserId, token))
            .ReturnsAsync(NotificationPreferences.Default)
            .Verifiable();
        SetupRateLimiter(allowed: true);

        // Act
        await CallDeliverAsync(cancellationToken: token);

        // Assert
        _mockPreferenceProvider.Verify(
            p => p.GetPreferencesAsync(TestUserId, token),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_CancellationTokenPassedToRateLimiter()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        _mockRateLimiter
            .Setup(r => r.TryAcquireAsync(TestUserId, token))
            .ReturnsAsync(true)
            .Verifiable();

        // Act
        await CallDeliverAsync(cancellationToken: token);

        // Assert
        _mockRateLimiter.Verify(
            r => r.TryAcquireAsync(TestUserId, token),
            Times.Once);
    }
}
