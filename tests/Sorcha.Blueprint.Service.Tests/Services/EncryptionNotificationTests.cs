// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class EncryptionNotificationTests
{
    private readonly Mock<IHubContext<BlueprintHub, IBlueprintHubClient>> _hubContext = new();
    private readonly Mock<IHubContext<EventsHub>> _eventsHubContext = new();
    private readonly Mock<IHubClients<IBlueprintHubClient>> _hubClients = new();
    private readonly Mock<IHubClients> _eventsHubClients = new();
    private readonly Mock<IBlueprintHubClient> _clientProxy = new();
    private readonly Mock<IClientProxy> _eventsClientProxy = new();
    private readonly NotificationService _service;

    public EncryptionNotificationTests()
    {
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _clientProxy
            .Setup(c => c.EncryptionProgress(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _clientProxy
            .Setup(c => c.EncryptionComplete(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _clientProxy
            .Setup(c => c.EncryptionFailed(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _eventsHubContext.Setup(h => h.Clients).Returns(_eventsHubClients.Object);
        _eventsHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_eventsClientProxy.Object);
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new NotificationService(
            _hubContext.Object,
            _eventsHubContext.Object,
            new Mock<IBlueprintInboxWriter>().Object,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task SendEncryptionProgress_SendsToCorrectWalletGroup()
    {
        var signal = new EncryptionSignal { OperationId = "op-1", PercentComplete = 30, Status = "encrypting" };

        await _service.NotifyEncryptionProgressAsync("wallet-test-001", signal);

        _hubClients.Verify(c => c.Group("wallet:wallet-test-001"), Times.Once);
        _clientProxy.Verify(c =>
            c.EncryptionProgress("op-1", It.IsAny<DateTimeOffset>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SendEncryptionComplete_IncludesOperationId()
    {
        var signal = new EncryptionSignal { OperationId = "op-2", PercentComplete = 100, Status = "complete" };

        await _service.NotifyEncryptionCompleteAsync("wallet-test-002", signal);

        _hubClients.Verify(c => c.Group("wallet:wallet-test-002"), Times.Once);
        _clientProxy.Verify(c =>
            c.EncryptionComplete("op-2", It.IsAny<DateTimeOffset>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SendEncryptionFailed_IncludesOperationId()
    {
        var signal = new EncryptionSignal { OperationId = "op-3", PercentComplete = 30, Status = "failed" };

        await _service.NotifyEncryptionFailedAsync("wallet-test-003", signal);

        _hubClients.Verify(c => c.Group("wallet:wallet-test-003"), Times.Once);
        _clientProxy.Verify(c =>
            c.EncryptionFailed("op-3", It.IsAny<DateTimeOffset>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SendEncryptionProgress_AllSteps_SendsCorrectOperationIds()
    {
        var operationIds = new[] { "op-a", "op-b", "op-c", "op-d" };

        foreach (var op in operationIds)
        {
            await _service.NotifyEncryptionProgressAsync("wallet-all-steps",
                new EncryptionSignal { OperationId = op, PercentComplete = 10, Status = "encrypting" });
        }

        _hubClients.Verify(c => c.Group("wallet:wallet-all-steps"), Times.Exactly(4));
        foreach (var op in operationIds)
        {
            _clientProxy.Verify(c =>
                c.EncryptionProgress(op, It.IsAny<DateTimeOffset>(), It.IsAny<string>()),
                Times.Once);
        }
    }
}
