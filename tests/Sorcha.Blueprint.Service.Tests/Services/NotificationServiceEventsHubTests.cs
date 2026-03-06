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
    private readonly Mock<IHubContext<ActionsHub>> _actionsHubContext = new();
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
        var notification = new EncryptionCompleteNotification
        {
            OperationId = "op-1",
            TransactionHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd"
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync("wallet-001", notification, userId: "user-42");

        // Assert — ActionsHub received EncryptionComplete
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
        var notification = new EncryptionFailedNotification
        {
            OperationId = "op-2",
            Error = "Key not found"
        };

        // Act
        await _service.NotifyEncryptionFailedAsync("wallet-002", notification, userId: "user-43");

        // Assert — ActionsHub received EncryptionFailed
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
        var notification = new EncryptionCompleteNotification
        {
            OperationId = "op-3",
            TransactionHash = "tx-hash-abc"
        };

        // Act
        await _service.NotifyEncryptionCompleteAsync("wallet-003", notification, userId: "user-99");

        // Assert — EventsHub group name is user:{userId}, NOT wallet:{address}
        _eventsHubClients.Verify(c => c.Group("user:user-99"), Times.Once);
        _eventsHubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("wallet:"))), Times.Never);
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_EventsHubMessage_GoesToUserGroup()
    {
        // Arrange
        var notification = new EncryptionFailedNotification
        {
            OperationId = "op-4",
            Error = "timeout"
        };

        // Act
        await _service.NotifyEncryptionFailedAsync("wallet-004", notification, userId: "user-100");

        // Assert — EventsHub group name is user:{userId}
        _eventsHubClients.Verify(c => c.Group("user:user-100"), Times.Once);
        _eventsHubClients.Verify(c => c.Group(It.Is<string>(g => g.StartsWith("wallet:"))), Times.Never);
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_EventsHubFailure_DoesNotPreventActionsHubNotification()
    {
        // Arrange — make EventsHub throw
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EventsHub connection lost"));

        var notification = new EncryptionCompleteNotification
        {
            OperationId = "op-5",
            TransactionHash = "tx-resilience"
        };

        // Act — should not throw
        var act = async () => await _service.NotifyEncryptionCompleteAsync("wallet-005", notification, userId: "user-50");
        await act.Should().NotThrowAsync();

        // Assert — ActionsHub still received its notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionComplete");
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_EventsHubFailure_DoesNotPreventActionsHubNotification()
    {
        // Arrange — make EventsHub throw
        _eventsClientProxy.Setup(c => c.SendCoreAsync(
                It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("EventsHub connection lost"));

        var notification = new EncryptionFailedNotification
        {
            OperationId = "op-6",
            Error = "P-256 key error"
        };

        // Act — should not throw
        var act = async () => await _service.NotifyEncryptionFailedAsync("wallet-006", notification, userId: "user-51");
        await act.Should().NotThrowAsync();

        // Assert — ActionsHub still received its notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionFailed");
    }

    [Fact]
    public async Task NotifyEncryptionCompleteAsync_WithoutUserId_SkipsEventsHub()
    {
        // Arrange
        var notification = new EncryptionCompleteNotification
        {
            OperationId = "op-7",
            TransactionHash = "tx-no-user"
        };

        // Act — no userId provided
        await _service.NotifyEncryptionCompleteAsync("wallet-007", notification);

        // Assert — ActionsHub received notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionComplete");

        // Assert — EventsHub was NOT called (no userId to target)
        _eventsMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyEncryptionFailedAsync_WithoutUserId_SkipsEventsHub()
    {
        // Arrange
        var notification = new EncryptionFailedNotification
        {
            OperationId = "op-8",
            Error = "key error"
        };

        // Act — no userId provided
        await _service.NotifyEncryptionFailedAsync("wallet-008", notification);

        // Assert — ActionsHub received notification
        _actionsMessages.Should().ContainSingle(m => m.Method == "EncryptionFailed");

        // Assert — EventsHub was NOT called
        _eventsMessages.Should().BeEmpty();
    }
}
