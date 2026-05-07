// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
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

public class EncryptionNotificationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Mock<IHubContext<BlueprintHub, IBlueprintHubClient>> _hubContext = new();
    private readonly Mock<IHubContext<EventsHub>> _eventsHubContext = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<ISubscriber> _subscriber = new();
    private readonly List<(RedisChannel Channel, string Message)> _published = new();
    private readonly NotificationService _service;

    public EncryptionNotificationTests()
    {
        _hubContext.Setup(h => h.Clients).Returns(Mock.Of<IHubClients<IBlueprintHubClient>>());
        _eventsHubContext.Setup(h => h.Clients).Returns(Mock.Of<IHubClients>());

        _redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Returns(_subscriber.Object);
        _subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((ch, msg, _) =>
                _published.Add((ch, msg.ToString()!)))
            .ReturnsAsync(0L);

        _service = new NotificationService(
            _hubContext.Object,
            _eventsHubContext.Object,
            new Mock<IBlueprintInboxWriter>().Object,
            _redis.Object,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task NotifyEncryptionProgressAsync_PublishesProgressEnvelopeToRedis()
    {
        var signal = new EncryptionSignal { OperationId = "op-1", PercentComplete = 30, Status = "encrypting" };

        await _service.NotifyEncryptionProgressAsync("wallet-test-001", signal);

        var (channel, message) = _published.Single();
        channel.ToString().Should().Be(EncryptionEventEnvelope.ChannelName);

        var envelope = JsonSerializer.Deserialize<EncryptionEventEnvelope>(message, JsonOptions)!;
        envelope.WalletAddress.Should().Be("wallet-test-001");
        envelope.OperationId.Should().Be("op-1");
        envelope.Kind.Should().Be(EncryptionEventEnvelope.KindProgress);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_PublishesCompleteEnvelope()
    {
        var signal = new EncryptionSignal { OperationId = "op-2", PercentComplete = 100, Status = "complete" };

        await _service.NotifyEncryptionCompleteAsync("wallet-test-002", signal);

        var envelope = JsonSerializer.Deserialize<EncryptionEventEnvelope>(_published.Single().Message, JsonOptions)!;
        envelope.WalletAddress.Should().Be("wallet-test-002");
        envelope.OperationId.Should().Be("op-2");
        envelope.Kind.Should().Be(EncryptionEventEnvelope.KindComplete);
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_PublishesFailedEnvelope()
    {
        var signal = new EncryptionSignal { OperationId = "op-3", PercentComplete = 30, Status = "failed" };

        await _service.NotifyEncryptionFailedAsync("wallet-test-003", signal);

        var envelope = JsonSerializer.Deserialize<EncryptionEventEnvelope>(_published.Single().Message, JsonOptions)!;
        envelope.WalletAddress.Should().Be("wallet-test-003");
        envelope.OperationId.Should().Be("op-3");
        envelope.Kind.Should().Be(EncryptionEventEnvelope.KindFailed);
    }

    [Fact]
    public async Task NotifyEncryptionProgressAsync_AllSteps_PublishesOnePerCall()
    {
        var operationIds = new[] { "op-a", "op-b", "op-c", "op-d" };

        foreach (var op in operationIds)
        {
            await _service.NotifyEncryptionProgressAsync("wallet-all-steps",
                new EncryptionSignal { OperationId = op, PercentComplete = 10, Status = "encrypting" });
        }

        _published.Should().HaveCount(4);
        _published.Select(p => JsonSerializer.Deserialize<EncryptionEventEnvelope>(p.Message, JsonOptions)!.OperationId)
            .Should().BeEquivalentTo(operationIds);
    }
}
