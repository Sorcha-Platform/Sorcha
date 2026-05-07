// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceDefaults.Hubs;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests verifying NotificationService publishes encryption envelopes to Redis
/// (consumed by Wallet Service's <c>EncryptionEventBridge</c>) AND keeps the
/// legacy EventsHub path alive when a userId is provided.
/// </summary>
public class NotificationServiceEventsHubTests
{
    private readonly Mock<IHubContext<BlueprintHub, IBlueprintHubClient>> _hubContext = new();
    private readonly Mock<IHubContext<EventsHub>> _eventsHubContext = new();
    private readonly Mock<IHubClients> _eventsHubClients = new();
    private readonly Mock<IClientProxy> _eventsClientProxy = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<ISubscriber> _subscriber = new();
    private readonly NotificationService _service;

    private readonly List<(string Method, object?[] Args)> _eventsMessages = [];
    private readonly List<(RedisChannel Channel, RedisValue Message)> _published = [];

    public NotificationServiceEventsHubTests()
    {
        _hubContext.Setup(h => h.Clients).Returns(Mock.Of<IHubClients<IBlueprintHubClient>>());

        _redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_subscriber.Object);
        _subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((ch, msg, _) => _published.Add((ch, msg)))
            .ReturnsAsync(0L);

        _eventsHubContext.Setup(h => h.Clients).Returns(_eventsHubClients.Object);
        _eventsHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_eventsClientProxy.Object);
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _eventsMessages.Add((method, args)))
            .Returns(Task.CompletedTask);

        _service = new NotificationService(
            _hubContext.Object,
            _eventsHubContext.Object,
            new Mock<IBlueprintInboxWriter>().Object,
            _redis.Object,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_WithUserId_PublishesAndSendsToEventsHub()
    {
        var signal = new EncryptionSignal { OperationId = "op-1", PercentComplete = 100, Status = "complete" };

        await _service.NotifyEncryptionCompleteAsync("wallet-001", signal, userId: "user-42");

        _published.Should().HaveCount(1);
        _published.Single().Channel.ToString().Should().Be(EncryptionEventEnvelope.ChannelName);

        _eventsHubClients.Verify(c => c.Group("user:user-42"), Times.Once);
        _eventsMessages.Should().ContainSingle(m => m.Method == "EncryptionOperationCompleted");
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_WithUserId_PublishesAndSendsToEventsHub()
    {
        var signal = new EncryptionSignal { OperationId = "op-2", PercentComplete = 30, Status = "failed" };

        await _service.NotifyEncryptionFailedAsync("wallet-002", signal, userId: "user-43");

        _published.Should().HaveCount(1);

        _eventsHubClients.Verify(c => c.Group("user:user-43"), Times.Once);
        _eventsMessages.Should().ContainSingle(m => m.Method == "EncryptionOperationCompleted");
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_EventsHubMessage_GoesToUserGroupNotWalletGroup()
    {
        var signal = new EncryptionSignal { OperationId = "op-3", PercentComplete = 100, Status = "complete" };

        await _service.NotifyEncryptionCompleteAsync("wallet-003", signal, userId: "user-99");

        _eventsHubClients.Verify(c => c.Group("user:user-99"), Times.Once);
        _eventsHubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("wallet:"))), Times.Never);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_EventsHubFailure_DoesNotPreventRedisPublish()
    {
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EventsHub connection lost"));

        var signal = new EncryptionSignal { OperationId = "op-5", PercentComplete = 100, Status = "complete" };

        var act = async () => await _service.NotifyEncryptionCompleteAsync("wallet-005", signal, userId: "user-50");
        await act.Should().NotThrowAsync();

        _published.Should().HaveCount(1);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_WithoutUserId_PublishesButSkipsEventsHub()
    {
        var signal = new EncryptionSignal { OperationId = "op-7", PercentComplete = 100, Status = "complete" };

        await _service.NotifyEncryptionCompleteAsync("wallet-007", signal);

        _published.Should().HaveCount(1);
        _eventsMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_WithoutUserId_PublishesButSkipsEventsHub()
    {
        var signal = new EncryptionSignal { OperationId = "op-8", PercentComplete = 30, Status = "failed" };

        await _service.NotifyEncryptionFailedAsync("wallet-008", signal);

        _published.Should().HaveCount(1);
        _eventsMessages.Should().BeEmpty();
    }
}
