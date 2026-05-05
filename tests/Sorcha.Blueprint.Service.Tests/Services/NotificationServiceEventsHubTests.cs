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

/// <summary>
/// Tests verifying NotificationService sends EncryptionOperationCompleted via EventsHub.
/// </summary>
public class NotificationServiceEventsHubTests
{
    private readonly Mock<IHubContext<BlueprintHub>> _actionsHubContext = new();
    private readonly Mock<IHubContext<EventsHub>> _eventsHubContext = new();
    private readonly Mock<IHubClients> _actionsHubClients = new();
    private readonly Mock<IHubClients> _eventsHubClients = new();
    private readonly Mock<IClientProxy> _actionsClientProxy = new();
    private readonly Mock<IClientProxy> _eventsClientProxy = new();
    private readonly NotificationService _service;

    private readonly List<(string Method, object?[] Args)> _actionsMessages = [];
    private readonly List<(string Method, object?[] Args)> _eventsMessages = [];

    public NotificationServiceEventsHubTests()
    {
        _actionsHubContext.Setup(h => h.Clients).Returns(_actionsHubClients.Object);
        _actionsHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_actionsClientProxy.Object);
        _actionsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _actionsMessages.Add((method, args)))
            .Returns(Task.CompletedTask);

        _eventsHubContext.Setup(h => h.Clients).Returns(_eventsHubClients.Object);
        _eventsHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_eventsClientProxy.Object);
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) => _eventsMessages.Add((method, args)))
            .Returns(Task.CompletedTask);

        _service = new NotificationService(
            _actionsHubContext.Object,
            _eventsHubContext.Object,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_WithUserId_SendsToBothHubs()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-1",
            PercentComplete = 100,
            Status = "complete"
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync("wallet-001", signal, userId: "user-42");

        // Assert — BlueprintHub received EncryptionComplete
        _actionsHubClients.Verify(c => c.Group("wallet:wallet-001"), Times.Once);
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionComplete");

        // Assert — EventsHub received EncryptionOperationCompleted
        _eventsHubClients.Verify(c => c.Group("user:user-42"), Times.Once);
        _eventsMessages.Should().ContainSingle(m => m.Method == "EncryptionOperationCompleted");
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_WithUserId_SendsToBothHubs()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-2",
            PercentComplete = 30,
            Status = "failed"
        };

        // Act
        await _service.NotifyEncryptionFailedAsync("wallet-002", signal, userId: "user-43");

        // Assert — BlueprintHub received EncryptionFailed
        _actionsHubClients.Verify(c => c.Group("wallet:wallet-002"), Times.Once);
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionFailed");

        // Assert — EventsHub received EncryptionOperationCompleted
        _eventsHubClients.Verify(c => c.Group("user:user-43"), Times.Once);
        _eventsMessages.Should().ContainSingle(m => m.Method == "EncryptionOperationCompleted");
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_EventsHubMessage_GoesToUserGroup()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-3",
            PercentComplete = 100,
            Status = "complete"
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync("wallet-003", signal, userId: "user-99");

        // Assert — EventsHub group name is user:{userId}, NOT wallet:{address}
        _eventsHubClients.Verify(c => c.Group("user:user-99"), Times.Once);
        _eventsHubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("wallet:"))), Times.Never);
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_EventsHubMessage_GoesToUserGroup()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-4",
            PercentComplete = 30,
            Status = "failed"
        };

        // Act
        await _service.NotifyEncryptionFailedAsync("wallet-004", signal, userId: "user-100");

        // Assert — EventsHub group name is user:{userId}
        _eventsHubClients.Verify(c => c.Group("user:user-100"), Times.Once);
        _eventsHubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("wallet:"))), Times.Never);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_EventsHubFailure_DoesNotPreventBlueprintHubNotification()
    {
        // Arrange — make EventsHub throw
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EventsHub connection lost"));

        var signal = new EncryptionSignal
        {
            OperationId = "op-5",
            PercentComplete = 100,
            Status = "complete"
        };

        // Act — should not throw
        var act = async () => await _service.NotifyEncryptionCompleteAsync("wallet-005", signal, userId: "user-50");
        await act.Should().NotThrowAsync();

        // Assert — BlueprintHub still received its notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionComplete");
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_EventsHubFailure_DoesNotPreventBlueprintHubNotification()
    {
        // Arrange — make EventsHub throw
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EventsHub connection lost"));

        var signal = new EncryptionSignal
        {
            OperationId = "op-6",
            PercentComplete = 30,
            Status = "failed"
        };

        // Act — should not throw
        var act = async () => await _service.NotifyEncryptionFailedAsync("wallet-006", signal, userId: "user-51");
        await act.Should().NotThrowAsync();

        // Assert — BlueprintHub still received its notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionFailed");
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_WithoutUserId_SkipsEventsHub()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-7",
            PercentComplete = 100,
            Status = "complete"
        };

        // Act — no userId provided
        await _service.NotifyEncryptionCompleteAsync("wallet-007", signal);

        // Assert — BlueprintHub received notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionComplete");

        // Assert — EventsHub was NOT called (no userId to target)
        _eventsMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_WithoutUserId_SkipsEventsHub()
    {
        // Arrange
        var signal = new EncryptionSignal
        {
            OperationId = "op-8",
            PercentComplete = 30,
            Status = "failed"
        };

        // Act — no userId provided
        await _service.NotifyEncryptionFailedAsync("wallet-008", signal);

        // Assert — BlueprintHub received notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionFailed");

        // Assert — EventsHub was NOT called
        _eventsMessages.Should().BeEmpty();
    }
}
