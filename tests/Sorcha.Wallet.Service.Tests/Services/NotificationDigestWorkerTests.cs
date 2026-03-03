// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Models;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Tests.Helpers;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

public class NotificationDigestWorkerTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ISubscriber> _mockSubscriber;
    private readonly Mock<ILogger<NotificationDigestWorker>> _mockLogger;

    private const string TestUserId = "user-001";
    private const string AnotherUserId = "user-002";
    private const string DigestKeyPrefix = "wallet:digest:";
    private const string DigestActiveUsersKey = "wallet:digest:active-users";
    private const string PubSubChannel = "wallet:notifications";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotificationDigestWorkerTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDatabase = new Mock<IDatabase>();
        _mockSubscriber = new Mock<ISubscriber>();
        _mockLogger = new Mock<ILogger<NotificationDigestWorker>>();

        _mockRedis
            .Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDatabase.Object);

        _mockRedis
            .Setup(r => r.GetSubscriber(It.IsAny<object>()))
            .Returns(_mockSubscriber.Object);
    }

    // ---------------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------------

    private NotificationDigestWorker CreateWorker(int checkIntervalMinutes = 5)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Notifications:DigestCheckIntervalMinutes"] = checkIntervalMinutes.ToString()
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new NotificationDigestWorker(
            _mockRedis.Object,
            configuration,
            new NotificationMetrics(new TestMeterFactory()),
            _mockLogger.Object);
    }

    private static InboundActionEvent CreateTestEvent(
        string userId = TestUserId,
        string? blueprintId = "bp-001",
        DateTimeOffset? timestamp = null)
    {
        return new InboundActionEvent
        {
            WalletAddress = "bc1qtest000000000000000000000000000000000",
            UserId = userId,
            TransactionId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            RegisterId = "reg-001",
            BlueprintId = blueprintId,
            InstanceId = "inst-001",
            ActionId = 1,
            NextActionId = 2,
            DocketNumber = 42,
            Timestamp = timestamp ?? new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)
        };
    }

    private void SetupActiveUsers(params string[] userIds)
    {
        var members = userIds.Select(id => (RedisValue)id).ToArray();
        _mockDatabase
            .Setup(db => db.SetMembersAsync(
                (RedisKey)DigestActiveUsersKey,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(members);

        // Default: SortedSetLengthAsync returns 0 (empty after processing) so user is removed from set
        _mockDatabase
            .Setup(db => db.SortedSetLengthAsync(
                It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);
    }

    private void SetupEmptyActiveUsers()
    {
        _mockDatabase
            .Setup(db => db.SetMembersAsync(
                (RedisKey)DigestActiveUsersKey,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());
    }

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
    // ProcessPendingDigestsAsync — Timer fires and processes pending digests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_PendingDigestExists_PublishesConsolidatedNotification()
    {
        // Arrange
        var event1 = CreateTestEvent(blueprintId: "bp-001");
        var event2 = CreateTestEvent(blueprintId: "bp-001");

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, event1, event2);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.Is<RedisValue>(v => VerifyDigestPayload(v, TestUserId, expectedEventCount: 2)),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_MultipleUsers_ProcessesAllDigests()
    {
        // Arrange
        var event1 = CreateTestEvent(userId: TestUserId, blueprintId: "bp-001");
        var event2 = CreateTestEvent(userId: AnotherUserId, blueprintId: "bp-002");

        SetupActiveUsers(TestUserId, AnotherUserId);
        SetupScriptResult(TestUserId, event1);
        SetupScriptResult(AnotherUserId, event2);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert — one publish per user
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Exactly(2));
    }

    // ---------------------------------------------------------------------------
    // ProcessPendingDigestsAsync — Events grouped by blueprint with counts
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_EventsFromMultipleBlueprints_GroupsByBlueprintInPayload()
    {
        // Arrange — 3 events across 2 blueprints
        var eventBp1a = CreateTestEvent(blueprintId: "bp-001");
        var eventBp1b = CreateTestEvent(blueprintId: "bp-001");
        var eventBp2 = CreateTestEvent(blueprintId: "bp-002");

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, eventBp1a, eventBp1b, eventBp2);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert
        capturedJson.Should().NotBeNull();
        var digest = JsonSerializer.Deserialize<DigestNotification>(capturedJson!, JsonOptions);
        digest.Should().NotBeNull();
        digest!.TotalEvents.Should().Be(3);
        digest.BlueprintGroups.Should().HaveCount(2);

        var bp1Group = digest.BlueprintGroups.Single(g => g.BlueprintId == "bp-001");
        bp1Group.ActionCount.Should().Be(2);

        var bp2Group = digest.BlueprintGroups.Single(g => g.BlueprintId == "bp-002");
        bp2Group.ActionCount.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // ProcessPendingDigestsAsync — Empty digest suppressed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_NoDigestKeys_DoesNotPublish()
    {
        // Arrange
        SetupEmptyActiveUsers();

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Should not publish when no digest keys exist");
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_DigestKeyExistsButEmpty_DoesNotPublish()
    {
        // Arrange — key exists but Lua script returns empty array
        SetupActiveUsers(TestUserId);
        SetupEmptyScriptResult(TestUserId);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()),
            Times.Never,
            "Should suppress empty digest when no events in sorted set");
    }

    // ---------------------------------------------------------------------------
    // ProcessPendingDigestsAsync — Atomic dequeue (no double delivery)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_AtomicDequeue_UsesLuaScriptWithCorrectKey()
    {
        // Arrange
        var testEvent = CreateTestEvent();

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, testEvent);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert — Lua script called with correct key for atomic dequeue
        _mockDatabase.Verify(
            db => db.ScriptEvaluateAsync(
                It.Is<string>(script =>
                    script.Contains("ZRANGEBYSCORE") && script.Contains("ZREMRANGEBYSCORE")),
                It.Is<RedisKey[]>(keys =>
                    keys.Length == 1 && keys[0].ToString() == $"{DigestKeyPrefix}{TestUserId}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Once,
            "Should use Lua script for atomic read+delete to prevent double delivery");
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_AtomicDequeue_PassesCurrentTimestampAsMaxScore()
    {
        // Arrange
        var testEvent = CreateTestEvent();

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, testEvent);

        RedisValue[]? capturedValues = null;
        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) =>
                capturedValues = values)
            .ReturnsAsync(RedisResult.Create(
                new[] { RedisResult.Create((RedisValue)JsonSerializer.Serialize(testEvent, JsonOptions)) }));

        var worker = CreateWorker();
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        await worker.ProcessPendingDigestsAsync();

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert — score should be a recent timestamp
        capturedValues.Should().NotBeNull();
        capturedValues.Should().HaveCount(1);

        var scoreValue = (long)capturedValues![0];
        scoreValue.Should().BeInRange(before, after,
            "Max score should be current timestamp for atomic dequeue window");
    }

    // ---------------------------------------------------------------------------
    // ProcessPendingDigestsAsync — Digest payload structure
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_SingleBlueprint_PublishesCorrectDigestStructure()
    {
        // Arrange
        var testEvent = CreateTestEvent(blueprintId: "bp-001",
            timestamp: new DateTimeOffset(2026, 3, 1, 14, 30, 0, TimeSpan.Zero));

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, testEvent);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert
        capturedJson.Should().NotBeNull();
        var digest = JsonSerializer.Deserialize<DigestNotification>(capturedJson!, JsonOptions);
        digest.Should().NotBeNull();
        digest!.UserId.Should().Be(TestUserId);
        digest.TotalEvents.Should().Be(1);
        digest.BlueprintGroups.Should().ContainSingle();
        digest.BlueprintGroups[0].BlueprintId.Should().Be("bp-001");
        digest.BlueprintGroups[0].ActionCount.Should().Be(1);
        digest.DigestTimestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_NullBlueprintId_GroupsUnderUnknown()
    {
        // Arrange — event with null blueprint ID
        var testEvent = CreateTestEvent(blueprintId: null);

        SetupActiveUsers(TestUserId);
        SetupScriptResult(TestUserId, testEvent);

        string? capturedJson = null;
        _mockSubscriber
            .Setup(s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((_, value, _) =>
                capturedJson = value.ToString())
            .ReturnsAsync(1);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert — null BlueprintId grouped under "unknown"
        capturedJson.Should().NotBeNull();
        var digest = JsonSerializer.Deserialize<DigestNotification>(capturedJson!, JsonOptions);
        digest!.BlueprintGroups.Should().ContainSingle()
            .Which.BlueprintId.Should().Be("unknown");
    }

    // ---------------------------------------------------------------------------
    // ProcessPendingDigestsAsync — Error handling
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDigestsAsync_NoActiveUsers_ReturnsWithoutError()
    {
        // Arrange — active-users set is empty
        SetupEmptyActiveUsers();

        var worker = CreateWorker();

        // Act
        var act = () => worker.ProcessPendingDigestsAsync();

        // Assert — should handle gracefully
        await act.Should().NotThrowAsync();
        _mockDatabase.Verify(
            db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_ScriptEvaluateThrows_ContinuesWithNextUser()
    {
        // Arrange — user-001 script fails, user-002 succeeds
        var event2 = CreateTestEvent(userId: AnotherUserId, blueprintId: "bp-002");

        SetupActiveUsers(TestUserId, AnotherUserId);

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k[0].ToString() == $"{DigestKeyPrefix}{TestUserId}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToResolvePhysicalConnection, "Test failure"));

        SetupScriptResult(AnotherUserId, event2);

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert — user-002 digest still delivered despite user-001 failure
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.Is<RedisValue>(v => VerifyDigestPayload(v, AnotherUserId, expectedEventCount: 1)),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessPendingDigestsAsync_MalformedJsonEntry_SkipsAndProcessesRemaining()
    {
        // Arrange — one valid event, one malformed JSON
        var validEvent = CreateTestEvent(blueprintId: "bp-001");
        var key = (RedisKey)$"{DigestKeyPrefix}{TestUserId}";

        SetupActiveUsers(TestUserId);

        var entries = new[]
        {
            RedisResult.Create((RedisValue)"{ this is not valid json }"),
            RedisResult.Create((RedisValue)JsonSerializer.Serialize(validEvent, JsonOptions))
        };

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k.Length == 1 && k[0] == key),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(entries));

        var worker = CreateWorker();

        // Act
        await worker.ProcessPendingDigestsAsync();

        // Assert — digest published with only the valid event
        _mockSubscriber.Verify(
            s => s.PublishAsync(
                It.Is<RedisChannel>(ch => ch.ToString() == PubSubChannel),
                It.Is<RedisValue>(v => VerifyDigestPayload(v, TestUserId, expectedEventCount: 1)),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------
    // Verification helper
    // ---------------------------------------------------------------------------

    private static bool VerifyDigestPayload(RedisValue value, string expectedUserId, int expectedEventCount)
    {
        try
        {
            var json = value.ToString();
            var digest = JsonSerializer.Deserialize<DigestNotification>(json, JsonOptions);
            return digest != null
                && digest.UserId == expectedUserId
                && digest.TotalEvents == expectedEventCount;
        }
        catch
        {
            return false;
        }
    }
}
