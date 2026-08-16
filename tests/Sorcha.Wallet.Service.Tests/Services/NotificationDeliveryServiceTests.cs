// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Participant;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for the post-T075 <see cref="NotificationDeliveryService"/> — real-time
/// notifications now write durable inbox entries via <see cref="IPlatformInboxClient"/>
/// instead of publishing to the legacy <c>wallet:notifications</c> Redis channel.
/// Digest queueing still uses Redis sorted sets (T076 will migrate that path).
/// </summary>
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
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<IParticipantServiceClient> _mockParticipants;
    private readonly Mock<IPlatformInboxClient> _mockInbox;
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
    private static readonly Guid TestUserIdentityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestPlatformUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset TestTimestamp = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public NotificationDeliveryServiceTests()
    {
        _mockWalletRepository = new Mock<IWalletRepository>();
        _mockRateLimiter = new Mock<INotificationRateLimiter>();
        _mockPreferenceProvider = new Mock<INotificationPreferenceProvider>();
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockParticipants = new Mock<IParticipantServiceClient>();
        _mockInbox = new Mock<IPlatformInboxClient>();
        _mockLogger = new Mock<ILogger<NotificationDeliveryService>>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_mockDatabase.Object);

        _service = new NotificationDeliveryService(
            _mockWalletRepository.Object,
            _mockRateLimiter.Object,
            _mockPreferenceProvider.Object,
            new NotificationMetrics(new TestMeterFactory()),
            _mockRedis.Object,
            _mockParticipants.Object,
            _mockInbox.Object,
            _mockLogger.Object);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static WalletEntity CreateTestWallet()
        => new()
        {
            Address = TestAddress,
            Owner = TestUserId,
            Tenant = TestTenantId,
            Name = "Test Wallet",
            EncryptedPrivateKey = "encrypted-key-data",
            EncryptionKeyId = "key-001",
            Algorithm = "ED25519"
        };

    private void SetupWalletFound(WalletEntity? wallet = null)
        => _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallet ?? CreateTestWallet());

    private void SetupWalletNotFound()
        => _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletEntity?)null);

    private void SetupPreferences(NotificationPreferences prefs)
        => _mockPreferenceProvider
            .Setup(p => p.GetPreferencesAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefs);

    private void SetupRateLimiter(bool allowed)
        => _mockRateLimiter
            .Setup(r => r.TryAcquireAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed);

    private void SetupParticipantResolution()
    {
        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(TestAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(),
                UserId = TestUserIdentityId,
                OrganizationId = Guid.NewGuid(),
                DisplayName = "Test",
                Email = "test@example.com",
                Status = "Active",
            });
        _mockInbox
            .Setup(i => i.ResolvePlatformUserIdAsync(TestUserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestPlatformUserId);
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));
    }

    private Task<NotificationDeliveryResult> CallDeliverAsync(
        bool isRecovery = false,
        CancellationToken cancellationToken = default)
        => _service.DeliverAsync(
            TestAddress, TestTxId, TestRegisterId, docketNumber: 42,
            TestBlueprintId, TestInstanceId, actionId: 1, nextActionId: 2,
            TestSenderAddress, TestTimestamp, isRecovery, cancellationToken);

    // ---------------------------------------------------------------------------
    // Real-time delivery — inbox write path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RealTime_WritesInboxEntryAndReturnsDeliveredRealTime()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.DeliveredRealTime);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_RealTime_BuildsExpectedInboxPayload()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        InboxWritePayload? captured = null;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await CallDeliverAsync();

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(TestPlatformUserId);
        captured.Category.Should().Be("Action");
        captured.Severity.Should().Be("ActionRequired");
        captured.CorrelationKey.Should().Be($"tx:{TestAddress}:{TestTxId}");
        captured.DetailHref.Should().Be($"/api/instances/{TestInstanceId}/actions/1");
        captured.OccurredAt.Should().Be(TestTimestamp);
        captured.Title.Should().Be("Action required");
        captured.IconKey.Should().Be("action.required");
        captured.SourceEventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DeliverAsync_RealTime_RecoveryEvent_UsesRecoveryTitle()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        InboxWritePayload? captured = null;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await CallDeliverAsync(isRecovery: true);

        captured!.Title.Should().Be("Recovered action requires your attention");
    }

    [Fact]
    public async Task DeliverAsync_RealTime_SourceEventIdIsDeterministicAcrossCalls()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        var captured = new List<Guid>();
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await CallDeliverAsync();
        await CallDeliverAsync();

        captured.Should().HaveCount(2);
        captured[0].Should().Be(captured[1]);
    }

    [Fact]
    public async Task DeliverAsync_RealTime_NullInstanceId_FallsBackToInboxDetailHref()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        InboxWritePayload? captured = null;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        await _service.DeliverAsync(
            TestAddress, TestTxId, TestRegisterId, docketNumber: 42,
            blueprintId: null, instanceId: null,
            actionId: 1, nextActionId: 2,
            senderAddress: null, TestTimestamp, isRecovery: false);

        captured!.DetailHref.Should().Be("/api/me/inbox");
    }

    [Fact]
    public async Task DeliverAsync_RealTime_DoesNotTouchDigestQueue()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();

        await CallDeliverAsync();

        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    // ---------------------------------------------------------------------------
    // Real-time delivery — degraded paths
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_RealTime_NoParticipant_ReturnsNoUserFound()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(TestAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverAsync_RealTime_NoPlatformUserResolution_ReturnsNoUserFound()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(TestAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(), UserId = TestUserIdentityId,
                OrganizationId = Guid.NewGuid(), DisplayName = "x",
                Email = "x@example.com", Status = "Active"
            });
        _mockInbox
            .Setup(i => i.ResolvePlatformUserIdAsync(TestUserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverAsync_RealTime_InboxWriteThrows_ReturnsNoUserFoundAndDoesNotPropagate()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unreachable"));

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.NoUserFound);
    }

    [Fact]
    public async Task DeliverAsync_RealTime_CancellationPropagates()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: true);
        SetupParticipantResolution();
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => CallDeliverAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------------------------------------------------------------------------
    // Digest queue — preserved for T076
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_DigestPrefs_QueuesToRedisSortedSet()
    {
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.QueuedForDigest);
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.Is<RedisKey>(k => k.ToString() == $"wallet:digest:{TestUserId}"),
                It.IsAny<RedisValue>(), It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()),
            Times.Once);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverAsync_DigestPrefs_AddsUserToActiveUsersSet()
    {
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        await CallDeliverAsync();

        _mockDatabase.Verify(
            db => db.SetAddAsync(
                (RedisKey)"wallet:digest:active-users",
                (RedisValue)TestUserId,
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task DeliverAsync_DigestPrefs_DoesNotCallRateLimiter()
    {
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });

        await CallDeliverAsync();

        _mockRateLimiter.Verify(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverAsync_RateLimited_OverflowsToDigest()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        SetupRateLimiter(allowed: false);

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.RateLimited);
        _mockDatabase.Verify(
            db => db.SortedSetAddAsync(
                It.Is<RedisKey>(k => k.ToString() == $"wallet:digest:{TestUserId}"),
                It.IsAny<RedisValue>(), It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()),
            Times.Once);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------
    // Wallet / preference short-circuits
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletNotFound_ReturnsNoUserFound()
    {
        SetupWalletNotFound();

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockPreferenceProvider.Verify(
            p => p.GetPreferencesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeliverAsync_NotificationsDisabled_ReturnsNoUserFoundAndDoesNotWriteInbox()
    {
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { NotificationsEnabled = false });

        var result = await CallDeliverAsync();

        result.Should().Be(NotificationDeliveryResult.NoUserFound);
        _mockRateLimiter.Verify(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockInbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------
    // Exception propagation from upstream collaborators
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeliverAsync_WalletRepositoryThrows_PropagatesException()
    {
        _mockWalletRepository
            .Setup(r => r.GetByAddressAsync(TestAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var act = () => CallDeliverAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Database connection failed");
    }

    [Fact]
    public async Task DeliverAsync_PreferenceProviderThrows_PropagatesException()
    {
        SetupWalletFound();
        _mockPreferenceProvider
            .Setup(p => p.GetPreferencesAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant service unavailable"));

        var act = () => CallDeliverAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task DeliverAsync_RateLimiterThrows_PropagatesException()
    {
        SetupWalletFound();
        SetupPreferences(NotificationPreferences.Default);
        _mockRateLimiter
            .Setup(r => r.TryAcquireAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, CommandFlags.None, "Connection lost"));

        var act = () => CallDeliverAsync();

        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task DeliverAsync_DigestSortedSetThrows_PropagatesException()
    {
        SetupWalletFound();
        SetupPreferences(new NotificationPreferences { IsRealTime = false });
        _mockDatabase
            .Setup(db => db.SortedSetAddAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(),
                It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(
                ConnectionFailureType.UnableToResolvePhysicalConnection, CommandFlags.None, "Connection lost"));

        var act = () => CallDeliverAsync();

        await act.Should().ThrowAsync<RedisConnectionException>();
    }
}
