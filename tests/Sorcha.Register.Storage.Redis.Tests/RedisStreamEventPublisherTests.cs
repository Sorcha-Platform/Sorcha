// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Storage.Redis;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Register.Storage.Redis.Tests;

public class RedisStreamEventPublisherTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly RedisEventStreamConfiguration _config;
    private readonly Mock<ILogger<RedisStreamEventPublisher>> _loggerMock;

    public RedisStreamEventPublisherTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _config = new RedisEventStreamConfiguration
        {
            StreamPrefix = "test:events:",
            ConsumerGroup = "test-service",
            MaxStreamLength = 500
        };
        _loggerMock = new Mock<ILogger<RedisStreamEventPublisher>>();
    }

    private RedisStreamEventPublisher CreatePublisher() =>
        new(_redisMock.Object, Options.Create(_config), _loggerMock.Object);

    private void SetupStreamAddDefault()
    {
        // Match all overload parameters (SE.Redis 2.10.x has 8 params)
        _dbMock.Setup(d => d.StreamAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<NameValueEntry[]>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<long?>(),
                It.IsAny<bool>(),
                It.IsAny<long?>(),
                It.IsAny<StreamTrimMode>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync("1-0");
    }

    [Fact]
    public async Task PublishAsync_ValidEvent_CallsStreamAddAsync()
    {
        SetupStreamAddDefault();

        var publisher = CreatePublisher();
        var evt = new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test Register",
            CreatedAt = DateTime.UtcNow
        };

        await publisher.PublishAsync("register:created", evt);

        _dbMock.Verify(d => d.StreamAddAsync(
            (RedisKey)"test:events:register:created",
            It.Is<NameValueEntry[]>(entries =>
                entries.Any(e => e.Name == "type" && e.Value == "RegisterCreatedEvent") &&
                entries.Any(e => e.Name == "data") &&
                entries.Any(e => e.Name == "timestamp") &&
                entries.Any(e => e.Name == "source" && e.Value == "test-service")),
            It.IsAny<RedisValue?>(),
            (long?)500,
            true,
            It.IsAny<long?>(),
            It.IsAny<StreamTrimMode>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_StreamKeyUsesConfiguredPrefix()
    {
        SetupStreamAddDefault();

        var publisher = CreatePublisher();
        var evt = new RegisterDeletedEvent
        {
            RegisterId = "reg-2",
            DeletedAt = DateTime.UtcNow
        };

        await publisher.PublishAsync("register:deleted", evt);

        _dbMock.Verify(d => d.StreamAddAsync(
            (RedisKey)"test:events:register:deleted",
            It.IsAny<NameValueEntry[]>(),
            It.IsAny<RedisValue?>(),
            It.IsAny<long?>(),
            It.IsAny<bool>(),
            It.IsAny<long?>(),
            It.IsAny<StreamTrimMode>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_TypeFieldSetToEventClassName()
    {
        SetupStreamAddDefault();

        var publisher = CreatePublisher();
        await publisher.PublishAsync("docket:confirmed", new DocketConfirmedEvent
        {
            RegisterId = "reg-1",
            DocketId = 5,
            Hash = "abc",
            TimeStamp = DateTime.UtcNow
        });

        _dbMock.Verify(d => d.StreamAddAsync(
            It.IsAny<RedisKey>(),
            It.Is<NameValueEntry[]>(entries =>
                entries.Any(e => e.Name == "type" && e.Value == "DocketConfirmedEvent")),
            It.IsAny<RedisValue?>(),
            It.IsAny<long?>(),
            It.IsAny<bool>(),
            It.IsAny<long?>(),
            It.IsAny<StreamTrimMode>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_DataFieldContainsJsonSerializedEvent()
    {
        NameValueEntry[]? capturedEntries = null;
        _dbMock.Setup(d => d.StreamAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<NameValueEntry[]>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<long?>(),
                It.IsAny<bool>(),
                It.IsAny<long?>(),
                It.IsAny<StreamTrimMode>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, NameValueEntry[], RedisValue?, long?, bool, long?, StreamTrimMode, CommandFlags>(
                (_, entries, _, _, _, _, _, _) => capturedEntries = entries)
            .ReturnsAsync("1-0");

        var publisher = CreatePublisher();
        await publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-99",
            Name = "My Register",
            CreatedAt = DateTime.UtcNow
        });

        capturedEntries.Should().NotBeNull();
        var dataEntry = capturedEntries!.First(e => e.Name == "data");
        var json = dataEntry.Value.ToString();
        json.Should().Contain("reg-99");
        json.Should().Contain("My Register");
    }

    [Fact]
    public async Task PublishAsync_MaxLenTrimmingParameterPassedCorrectly()
    {
        _config.MaxStreamLength = 2000;
        SetupStreamAddDefault();

        var publisher = CreatePublisher();
        await publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test"
        });

        _dbMock.Verify(d => d.StreamAddAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<NameValueEntry[]>(),
            It.IsAny<RedisValue?>(),
            (long?)2000,
            true,
            It.IsAny<long?>(),
            It.IsAny<StreamTrimMode>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_RedisUnavailable_DoesNotThrow()
    {
        _dbMock.Setup(d => d.StreamAddAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<NameValueEntry[]>(),
                It.IsAny<RedisValue?>(),
                It.IsAny<long?>(),
                It.IsAny<bool>(),
                It.IsAny<long?>(),
                It.IsAny<StreamTrimMode>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Connection refused"));

        var publisher = CreatePublisher();

        var act = () => publisher.PublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "Test"
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var publisher = CreatePublisher();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            publisher.PublishAsync("register:created", new RegisterCreatedEvent
            {
                RegisterId = "reg-1",
                Name = "Test"
            }, cts.Token));
    }

    [Fact]
    public void Constructor_NullRedis_ThrowsArgumentNullException()
    {
        var act = () => new RedisStreamEventPublisher(
            null!,
            Options.Create(_config),
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("redis");
    }
}
