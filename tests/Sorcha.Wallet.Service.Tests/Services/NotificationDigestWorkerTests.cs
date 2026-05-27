// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Participant;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for the post-T076 <see cref="NotificationDigestWorker"/> — drained
/// digest events now produce a single durable inbox entry per user per cycle
/// instead of being republished to the legacy <c>wallet:notifications</c>
/// Redis pub/sub channel.
/// </summary>
public class NotificationDigestWorkerTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis = new();
    private readonly Mock<IDatabase> _mockDatabase = new();
    private readonly Mock<ISubscriber> _mockSubscriber = new();
    private readonly Mock<IParticipantServiceClient> _mockParticipants = new();
    private readonly Mock<IPlatformInboxClient> _mockInbox = new();
    private readonly Mock<ILogger<NotificationDigestWorker>> _mockLogger = new();

    private const string TestUserId = "user-001";
    private const string AnotherUserId = "user-002";
    private const string TestWallet = "bc1qtest000000000000000000000000000000000";
    private const string DigestKeyPrefix = "wallet:digest:";
    private const string DigestActiveUsersKey = "wallet:digest:active-users";

    private static readonly Guid TestUserIdentityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestPlatformUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotificationDigestWorkerTests()
    {
        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);
        _mockRedis.Setup(r => r.GetSubscriber(It.IsAny<object>()))
            .Returns(_mockSubscriber.Object);

        // Default: participant + inbox resolution succeed for TestWallet.
        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(TestWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(),
                UserId = TestUserIdentityId,
                OrganizationId = Guid.NewGuid(),
                DisplayName = "x",
                Email = "x@example.com",
                Status = "Active",
            });
        _mockInbox
            .Setup(i => i.ResolvePlatformUserIdAsync(TestUserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestPlatformUserId);
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));
    }

    private NotificationDigestWorker CreateWorker(int checkIntervalMinutes = 5)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Notifications:DigestCheckIntervalMinutes"] = checkIntervalMinutes.ToString()
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var services = new ServiceCollection();
        services.AddSingleton(_mockParticipants.Object);
        services.AddSingleton(_mockInbox.Object);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new NotificationDigestWorker(
            _mockRedis.Object,
            configuration,
            new NotificationMetrics(new TestMeterFactory()),
            scopeFactory,
            _mockLogger.Object);
    }

    private static InboundActionEvent CreateTestEvent(
        string userId = TestUserId,
        string? blueprintId = "bp-001",
        string? walletAddress = null,
        DateTimeOffset? timestamp = null,
        string? transactionId = null)
        => new()
        {
            WalletAddress = walletAddress ?? TestWallet,
            UserId = userId,
            TransactionId = transactionId ?? "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            RegisterId = "reg-001",
            BlueprintId = blueprintId,
            InstanceId = "inst-001",
            ActionId = 1,
            NextActionId = 2,
            DocketNumber = 42,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)
        };

    private void SetupActiveUsers(params string[] userIds)
    {
        var members = userIds.Select(id => (RedisValue)id).ToArray();
        _mockDatabase
            .Setup(db => db.SetMembersAsync((RedisKey)DigestActiveUsersKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(members);

        _mockDatabase
            .Setup(db => db.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);
    }

    private void SetupEmptyActiveUsers()
        => _mockDatabase
            .Setup(db => db.SetMembersAsync((RedisKey)DigestActiveUsersKey, It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

    private void SetupScriptResult(string userId, params InboundActionEvent[] events)
    {
        var key = (RedisKey)$"{DigestKeyPrefix}{userId}";
        var serialized = events
            .Select(e => RedisResult.Create((RedisValue)JsonSerializer.Serialize(e, JsonOptions)))
            .ToArray();

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k.Length == 1 && k[0] == key),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(serialized));
    }

    private void SetupEmptyScriptResult(string userId)
    {
        var key = (RedisKey)$"{DigestKeyPrefix}{userId}";
        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k.Length == 1 && k[0] == key),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(Array.Empty<RedisResult>()));
    }

    // ---------------------------------------------------------------------------
    // Inbox write — happy paths
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_PendingEvents_WritesOneInboxEntryPerUser()
    {
        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, CreateTestEvent(), CreateTestEvent(transactionId: "tx2"));

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockInbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockSubscriber.Verify(
            s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Never,
            "Legacy wallet:notifications publish must be gone post-T076");
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_BuildsExpectedPayloadShape()
    {
        var ts = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId,
            CreateTestEvent(blueprintId: "bp-001", timestamp: ts, transactionId: "tx1"),
            CreateTestEvent(blueprintId: "bp-002", timestamp: ts.AddMinutes(1), transactionId: "tx2"));

        InboxWritePayload? captured = null;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(TestPlatformUserId);
        captured.Category.Should().Be("Action");
        captured.Severity.Should().Be("Info");
        captured.Title.Should().Be("2 actions awaiting your attention");
        captured.Summary.Should().Be("Across 2 blueprints");
        captured.DetailHref.Should().Be("/api/me/inbox");
        captured.IconKey.Should().Be("action.digest");
        captured.OccurredAt.Should().Be(ts.AddMinutes(1));
        captured.CorrelationKey.Should().Be($"digest:{TestUserId}:{ts.AddMinutes(1).ToUnixTimeMilliseconds()}");
        captured.ChannelHints.Should().Be(1 | 8, "Inbox|Digest = 9");
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_SingleEventSingleBlueprint_OmitsAcrossBlueprintsSummary()
    {
        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, CreateTestEvent(blueprintId: "bp-001"));

        InboxWritePayload? captured = null;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        captured!.Title.Should().Be("1 action awaiting your attention");
        captured.Summary.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_SourceEventIdIsDeterministicAcrossRuns()
    {
        SetupActiveUsers(TestUserId);
        var e1 = CreateTestEvent(transactionId: "tx-a");
        var e2 = CreateTestEvent(transactionId: "tx-b");
        SetupScriptResult(TestUserId, e1, e2);

        var captured = new List<Guid>();
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        // Reset the sorted-set length stub so the second sweep behaves the same way.
        await worker.ProcessPendingDigestsAsync();

        captured.Should().HaveCount(2);
        captured[0].Should().Be(captured[1]);
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_MultipleUsers_WritesOneEntryPerUser()
    {
        var anotherWallet = "bc1qother00000000000000000000000000000000";
        var anotherIdentity = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var anotherPlatform = Guid.Parse("44444444-4444-4444-4444-444444444444");

        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(anotherWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParticipantInfo
            {
                Id = Guid.NewGuid(), UserId = anotherIdentity,
                OrganizationId = Guid.NewGuid(), DisplayName = "y",
                Email = "y@example.com", Status = "Active"
            });
        _mockInbox
            .Setup(i => i.ResolvePlatformUserIdAsync(anotherIdentity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(anotherPlatform);

        SetupActiveUsers(TestUserId, AnotherUserId);
        SetupScriptResult(TestUserId, CreateTestEvent(userId: TestUserId));
        SetupScriptResult(AnotherUserId, CreateTestEvent(userId: AnotherUserId, walletAddress: anotherWallet));

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockInbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ---------------------------------------------------------------------------
    // No-op paths
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_NoActiveUsers_DoesNotWriteInbox()
    {
        SetupEmptyActiveUsers();
        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockInbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_EmptyScriptResult_DoesNotWriteInbox()
    {
        SetupActiveUsers(TestUserId);
        SetupEmptyScriptResult(TestUserId);

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockInbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------------------------
    // Atomic dequeue preserved
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_StillUsesAtomicDequeueLuaScript()
    {
        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, CreateTestEvent());

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockDatabase.Verify(
            db => db.ScriptEvaluateAsync(
                It.Is<string>(script => script.Contains("ZRANGEBYSCORE") && script.Contains("ZREMRANGEBYSCORE")),
                It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString() == $"{DigestKeyPrefix}{TestUserId}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------
    // Degraded paths
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_NoParticipantForWallet_StillRemovesUserFromActiveSet()
    {
        _mockParticipants
            .Setup(p => p.GetByWalletAddressAsync(TestWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantInfo?)null);

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, CreateTestEvent());

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        _mockInbox.Verify(
            i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockDatabase.Verify(
            db => db.SetRemoveAsync(
                (RedisKey)DigestActiveUsersKey,
                (RedisValue)TestUserId,
                It.IsAny<CommandFlags>()),
            Times.Once,
            "Active-users flag must clear once the queue drains, even if inbox-write was skipped");
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_InboxWriteThrows_DoesNotPropagateOrLeakBetweenUsers()
    {
        SetupActiveUsers(TestUserId, AnotherUserId);
        SetupScriptResult(TestUserId, CreateTestEvent());
        SetupScriptResult(AnotherUserId, CreateTestEvent(userId: AnotherUserId));

        var calls = 0;
        _mockInbox
            .Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Returns<InboxWritePayload, CancellationToken>((_, _) =>
            {
                if (++calls == 1) throw new HttpRequestException("Tenant unreachable");
                return Task.FromResult(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));
            });

        var worker = CreateWorker();
        await worker.ProcessPendingDigestsAsync();

        calls.Should().Be(2);
    }
}
