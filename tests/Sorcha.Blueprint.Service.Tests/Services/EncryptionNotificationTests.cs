// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class EncryptionNotificationTests
{
    private readonly Mock<IHubContext<BlueprintHub>> _hubContext = new();
    private readonly Mock<IHubContext<EventsHub>> _eventsHubContext = new();
    private readonly Mock<IHubClients> _hubClients = new();
    private readonly Mock<IHubClients> _eventsHubClients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();
    private readonly Mock<IClientProxy> _eventsClientProxy = new();
    private readonly NotificationService _service;

    private readonly List<(string Method, object?[] Args)> _sentMessages = [];

    public EncryptionNotificationTests()
    {
        _hubContext.Setup(h => h.Clients).Returns(_hubClients.Object);
        _hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);
        _clientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _sentMessages.Add((method, args)))
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
        // Arrange
        var walletAddress = "wallet-test-001";
        var signal = new EncryptionSignal
        {
            OperationId = "op-1",
            PercentComplete = 30,
            Status = "encrypting"
        };

        // Act
        await _service.NotifyEncryptionProgressAsync(walletAddress, signal);

        // Assert — correct group name: wallet:{address}
        _hubClients.Verify(c => c.Group("wallet:wallet-test-001"), Times.Once);

        // Assert — correct event name and payload
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionProgress",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionProgress");
        var payload = sent.Args[0].Should().BeOfType<EncryptionSignal>().Subject;
        payload.OperationId.Should().Be("op-1");
        payload.PercentComplete.Should().Be(30);
        payload.Status.Should().Be("encrypting");
    }

    [Fact]
    public async Task SendEncryptionComplete_IncludesOperationId()
    {
        // Arrange
        var walletAddress = "wallet-test-002";
        var signal = new EncryptionSignal
        {
            OperationId = "op-2",
            PercentComplete = 100,
            Status = "complete"
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync(walletAddress, signal);

        // Assert — correct group
        _hubClients.Verify(c => c.Group("wallet:wallet-test-002"), Times.Once);

        // Assert — payload includes operation id and status
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionComplete",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionComplete");
        var payload = sent.Args[0].Should().BeOfType<EncryptionSignal>().Subject;
        payload.OperationId.Should().Be("op-2");
        payload.PercentComplete.Should().Be(100);
        payload.Status.Should().Be("complete");
    }

    [Fact]
    public async Task SendEncryptionFailed_IncludesFailedStatus()
    {
        // Arrange
        var walletAddress = "wallet-test-003";
        var signal = new EncryptionSignal
        {
            OperationId = "op-3",
            PercentComplete = 30,
            Status = "failed"
        };

        // Act
        await _service.NotifyEncryptionFailedAsync(walletAddress, signal);

        // Assert — correct group
        _hubClients.Verify(c => c.Group("wallet:wallet-test-003"), Times.Once);

        // Assert — payload includes failed status
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionFailed",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionFailed");
        var payload = sent.Args[0].Should().BeOfType<EncryptionSignal>().Subject;
        payload.OperationId.Should().Be("op-3");
        payload.Status.Should().Be("failed");
        payload.PercentComplete.Should().Be(30);
    }

    [Fact]
    public async Task SendEncryptionProgress_AllSteps_SendsCorrectPercentages()
    {
        // Arrange & Act — send all 4 steps
        var steps = new[]
        {
            (pct: 10, status: "encrypting"),
            (pct: 30, status: "encrypting"),
            (pct: 60, status: "encrypting"),
            (pct: 80, status: "encrypting")
        };

        foreach (var (pct, status) in steps)
        {
            await _service.NotifyEncryptionProgressAsync("wallet-all-steps",
                new EncryptionSignal
                {
                    OperationId = "op-steps",
                    PercentComplete = pct,
                    Status = status
                });
        }

        // Assert — 4 progress calls to the same group
        _hubClients.Verify(c => c.Group("wallet:wallet-all-steps"), Times.Exactly(4));

        // Assert — each step sent correctly
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionProgress",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }
}
