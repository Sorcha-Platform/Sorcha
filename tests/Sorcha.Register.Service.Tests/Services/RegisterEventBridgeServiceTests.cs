// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Service.Hubs;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class RegisterEventBridgeServiceTests
{
    private readonly InMemoryEventSubscriber _subscriber;
    private readonly InMemoryEventPublisher _publisher;
    private readonly Mock<IHubContext<RegisterHub, IRegisterHubClient>> _hubContextMock;
    private readonly Mock<ILogger<RegisterEventBridgeService>> _loggerMock;
    private readonly Mock<IRegisterHubClient> _clientProxyMock;

    public RegisterEventBridgeServiceTests()
    {
        _subscriber = new InMemoryEventSubscriber();
        _publisher = new InMemoryEventPublisher(_subscriber);
        _hubContextMock = new Mock<IHubContext<RegisterHub, IRegisterHubClient>>();
        _loggerMock = new Mock<ILogger<RegisterEventBridgeService>>();
        _clientProxyMock = new Mock<IRegisterHubClient>();

        // Setup hub context to return our mock client for any group
        var clientsMock = new Mock<IHubClients<IRegisterHubClient>>();
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
    }

    private async Task StartBridgeAndPublishAsync<TEvent>(string topic, TEvent evt) where TEvent : class
    {
        var bridge = new RegisterEventBridgeService(_subscriber, _hubContextMock.Object, _loggerMock.Object);

        using var cts = new CancellationTokenSource();
        // ExecuteAsync registers subscriptions synchronously, then returns
        await bridge.StartAsync(cts.Token);
        // Small delay to ensure subscriptions are registered
        await Task.Delay(50);

        // Publish event — dispatched synchronously via InMemoryEventPublisher
        await _publisher.PublishAsync(topic, evt);

        await bridge.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RegisterCreatedEvent_BroadcastsRegisterCreated()
    {
        await StartBridgeAndPublishAsync("register:created", new RegisterCreatedEvent
        {
            RegisterId = "reg-1",
            Name = "My Register",

            CreatedAt = DateTime.UtcNow
        });

        _clientProxyMock.Verify(c => c.RegisterCreated("reg-1", "My Register"), Times.Once);
    }

    [Fact]
    public async Task RegisterDeletedEvent_BroadcastsRegisterDeleted()
    {
        await StartBridgeAndPublishAsync("register:deleted", new RegisterDeletedEvent
        {
            RegisterId = "reg-2",

            DeletedAt = DateTime.UtcNow
        });

        _clientProxyMock.Verify(c => c.RegisterDeleted("reg-2"), Times.Once);
    }

    [Fact]
    public async Task TransactionConfirmedEvent_BroadcastsTransactionConfirmed()
    {
        await StartBridgeAndPublishAsync("transaction:confirmed", new TransactionConfirmedEvent
        {
            TransactionId = "tx-1",
            RegisterId = "reg-3",
            SenderWallet = "wallet-1",
            PreviousTransactionId = "tx-0",
            ConfirmedAt = DateTime.UtcNow
        });

        _clientProxyMock.Verify(c => c.TransactionConfirmed("reg-3", "tx-1"), Times.Once);
    }

    [Fact]
    public async Task DocketConfirmedEvent_BroadcastsDocketSealed()
    {
        await StartBridgeAndPublishAsync("docket:confirmed", new DocketConfirmedEvent
        {
            RegisterId = "reg-4",
            DocketId = 7,
            Hash = "abc123",
            TimeStamp = DateTime.UtcNow
        });

        _clientProxyMock.Verify(c => c.DocketSealed("reg-4", 7UL, "abc123"), Times.Once);
    }

    [Fact]
    public async Task RegisterStatusChangedEvent_BroadcastsRegisterStatusChanged()
    {
        await StartBridgeAndPublishAsync("register:status-changed", new RegisterStatusChangedEvent
        {
            RegisterId = "reg-5",

            OldStatus = "Offline",
            NewStatus = "Online",
            ChangedAt = DateTime.UtcNow
        });

        _clientProxyMock.Verify(c => c.RegisterStatusChanged("reg-5", "Online"), Times.Once);
    }
}
