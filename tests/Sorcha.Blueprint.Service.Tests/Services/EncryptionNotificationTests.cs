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
    private readonly Mock<IHubContext<ActionsHub>> _hubContext = new();
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
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task SendEncryptionProgress_SendsToCorrectWalletGroup()
    {
        // Arrange
        var walletAddress = "wallet-test-001";
        var notification = new EncryptionProgressNotification
        {
            OperationId = "op-1",
            Step = 2,
            StepName = "Encrypting payloads",
            TotalSteps = 4,
            PercentComplete = 30
        };

        // Act
        await _service.NotifyEncryptionProgressAsync(walletAddress, notification);

        // Assert — correct group name: wallet:{address}
        _hubClients.Verify(c => c.Group("wallet:wallet-test-001"), Times.Once);

        // Assert — correct event name and payload
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionProgress",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionProgress");
        var payload = sent.Args[0].Should().BeOfType<EncryptionProgressNotification>().Subject;
        payload.OperationId.Should().Be("op-1");
        payload.Step.Should().Be(2);
        payload.StepName.Should().Be("Encrypting payloads");
        payload.TotalSteps.Should().Be(4);
        payload.PercentComplete.Should().Be(30);
    }

    [Fact]
    public async Task SendEncryptionComplete_IncludesTransactionHash()
    {
        // Arrange
        var walletAddress = "wallet-test-002";
        var txHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";
        var notification = new EncryptionCompleteNotification
        {
            OperationId = "op-2",
            TransactionHash = txHash
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync(walletAddress, notification);

        // Assert — correct group
        _hubClients.Verify(c => c.Group("wallet:wallet-test-002"), Times.Once);

        // Assert — payload includes transaction hash
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionComplete",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionComplete");
        var payload = sent.Args[0].Should().BeOfType<EncryptionCompleteNotification>().Subject;
        payload.OperationId.Should().Be("op-2");
        payload.TransactionHash.Should().Be(txHash);
    }

    [Fact]
    public async Task SendEncryptionFailed_IncludesErrorAndRecipient()
    {
        // Arrange
        var walletAddress = "wallet-test-003";
        var notification = new EncryptionFailedNotification
        {
            OperationId = "op-3",
            Error = "P-256 key not available for recipient",
            FailedRecipient = "wallet-recipient-fail",
            Step = 2
        };

        // Act
        await _service.NotifyEncryptionFailedAsync(walletAddress, notification);

        // Assert — correct group
        _hubClients.Verify(c => c.Group("wallet:wallet-test-003"), Times.Once);

        // Assert — payload includes error details
        _clientProxy.Verify(c => c.SendCoreAsync(
            "EncryptionFailed",
            It.Is<object?[]>(args => args.Length == 1 && args[0] != null),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify captured payload shape
        var sent = _sentMessages.Single(m => m.Method == "EncryptionFailed");
        var payload = sent.Args[0].Should().BeOfType<EncryptionFailedNotification>().Subject;
        payload.OperationId.Should().Be("op-3");
        payload.Error.Should().Be("P-256 key not available for recipient");
        payload.FailedRecipient.Should().Be("wallet-recipient-fail");
        payload.Step.Should().Be(2);
    }

    [Fact]
    public async Task SendEncryptionProgress_AllSteps_SendsCorrectPercentages()
    {
        // Arrange & Act — send all 4 steps
        var steps = new[]
        {
            (step: 1, name: "Resolving recipient keys", pct: 10),
            (step: 2, name: "Encrypting payloads", pct: 30),
            (step: 3, name: "Building transaction", pct: 60),
            (step: 4, name: "Signing and submitting", pct: 80)
        };

        foreach (var (step, name, pct) in steps)
        {
            await _service.NotifyEncryptionProgressAsync("wallet-all-steps",
                new EncryptionProgressNotification
                {
                    OperationId = "op-steps",
                    Step = step,
                    StepName = name,
                    TotalSteps = 4,
                    PercentComplete = pct
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
