// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Storage.Redis;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Register.Storage.Redis.Tests;

public class RedisStreamEventSubscriberTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly RedisEventStreamConfiguration _config;
    private readonly Mock<ILogger<RedisStreamEventSubscriber>> _loggerMock;

    public RedisStreamEventSubscriberTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _config = new RedisEventStreamConfiguration
        {
            StreamPrefix = "test:events:",
            ConsumerGroup = "test-service",
            BatchSize = 5,
            ReadBlockMilliseconds = 100,
            PendingIdleTimeout = TimeSpan.FromSeconds(5)
        };
        _loggerMock = new Mock<ILogger<RedisStreamEventSubscriber>>();
    }

    private RedisStreamEventSubscriber CreateSubscriber() =>
        new(_redisMock.Object, Options.Create(_config), _loggerMock.Object);

    /// <summary>
    /// Creates a RedisStream via reflection (constructor is internal in StackExchange.Redis)
    /// </summary>
    private static RedisStream CreateRedisStream(string key, StreamEntry[] entries)
    {
        var ctor = typeof(RedisStream).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 2);
        return (RedisStream)ctor.Invoke([(RedisKey)key, entries]);
    }

    private void SetupConsumerGroupCreation()
    {
        _dbMock.Setup(d => d.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
    }

    private StreamEntry CreateStreamEntry(string id, string eventType, string json)
    {
        return new StreamEntry(id,
        [
            new NameValueEntry("type", eventType),
            new NameValueEntry("data", json),
            new NameValueEntry("timestamp", "2026-02-14T00:00:00Z"),
            new NameValueEntry("source", "register-service")
        ]);
    }

    private void SetupStreamReadEmpty()
    {
        _dbMock.Setup(d => d.StreamReadGroupAsync(
                It.IsAny<StreamPosition[]>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisStream>());
    }

    private void SetupStreamReadSingleMessage(string streamKey, string eventType, string json)
    {
        var callCount = 0;
        _dbMock.Setup(d => d.StreamReadGroupAsync(
                It.IsAny<StreamPosition[]>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return [CreateRedisStream(streamKey, [CreateStreamEntry("1-0", eventType, json)])];
                }
                return Array.Empty<RedisStream>();
            });
    }

    [Fact]
    public async Task SubscribeAsync_RegistersHandler()
    {
        var subscriber = CreateSubscriber();
        var handlerCalled = false;

        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => { handlerCalled = true; return Task.CompletedTask; });

        // Handler registered but not yet called (no processing loop)
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task StartProcessingAsync_NoSubscriptions_ReturnsImmediately()
    {
        var subscriber = CreateSubscriber();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await subscriber.StartProcessingAsync(cts.Token);

        _dbMock.Verify(d => d.StreamCreateConsumerGroupAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(),
            It.IsAny<bool>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task StartProcessingAsync_CreatesConsumerGroup()
    {
        var subscriber = CreateSubscriber();
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => Task.CompletedTask);

        SetupConsumerGroupCreation();
        SetupStreamReadEmpty();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await subscriber.StartProcessingAsync(cts.Token);

        _dbMock.Verify(d => d.StreamCreateConsumerGroupAsync(
            "test:events:register:created",
            "test-service",
            "$",
            true,
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task StartProcessingAsync_BusyGroupError_HandledGracefully()
    {
        var subscriber = CreateSubscriber();
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => Task.CompletedTask);

        _dbMock.Setup(d => d.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<bool>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisServerException("BUSYGROUP Consumer Group name already exists"));

        SetupStreamReadEmpty();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => subscriber.StartProcessingAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartProcessingAsync_MessageDelivered_HandlerInvoked()
    {
        var subscriber = CreateSubscriber();
        RegisterCreatedEvent? receivedEvent = null;

        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            e => { receivedEvent = e; return Task.CompletedTask; });

        SetupConsumerGroupCreation();
        SetupStreamReadSingleMessage(
            "test:events:register:created",
            "RegisterCreatedEvent",
            """{"registerId":"reg-42","name":"TestReg","tenantId":"t-1"}""");

        _dbMock.Setup(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await subscriber.StartProcessingAsync(cts.Token);

        receivedEvent.Should().NotBeNull();
        receivedEvent!.RegisterId.Should().Be("reg-42");
        receivedEvent.Name.Should().Be("TestReg");
    }

    [Fact]
    public async Task StartProcessingAsync_SuccessfulHandler_AcknowledgesMessage()
    {
        var subscriber = CreateSubscriber();
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => Task.CompletedTask);

        SetupConsumerGroupCreation();
        SetupStreamReadSingleMessage(
            "test:events:register:created",
            "RegisterCreatedEvent",
            """{"registerId":"r1","name":"R","tenantId":"t"}""");

        _dbMock.Setup(d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await subscriber.StartProcessingAsync(cts.Token);

        _dbMock.Verify(d => d.StreamAcknowledgeAsync(
            "test:events:register:created",
            "test-service",
            "1-0",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task StartProcessingAsync_FailedHandler_DoesNotAcknowledge()
    {
        var subscriber = CreateSubscriber();
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => throw new InvalidOperationException("Handler failed"));

        SetupConsumerGroupCreation();
        SetupStreamReadSingleMessage(
            "test:events:register:created",
            "RegisterCreatedEvent",
            """{"registerId":"r1","name":"R","tenantId":"t"}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await subscriber.StartProcessingAsync(cts.Token);

        _dbMock.Verify(d => d.StreamAcknowledgeAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task StartProcessingAsync_Cancellation_ExitsCleanly()
    {
        var subscriber = CreateSubscriber();
        await subscriber.SubscribeAsync<RegisterCreatedEvent>(
            "register:created",
            _ => Task.CompletedTask);

        SetupConsumerGroupCreation();
        SetupStreamReadEmpty();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => subscriber.StartProcessingAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_NullRedis_ThrowsArgumentNullException()
    {
        var act = () => new RedisStreamEventSubscriber(
            null!,
            Options.Create(_config),
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("redis");
    }
}
