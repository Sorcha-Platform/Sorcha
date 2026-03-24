// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for RegisterHubConnection and ConnectionState.
/// Note: Full SignalR integration tests require E2E testing with a live hub.
/// </summary>
public class RegisterHubConnectionTests
{
    private readonly Mock<ILogger<RegisterHubConnection>> _mockLogger;

    public RegisterHubConnectionTests()
    {
        _mockLogger = new Mock<ILogger<RegisterHubConnection>>();
    }

    #region ConnectionState Tests

    [Fact]
    public void ConnectionState_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var state = new ConnectionState();

        // Assert
        state.Status.Should().Be(ConnectionStatus.Disconnected);
        state.LastConnected.Should().BeNull();
        state.ReconnectAttempts.Should().Be(0);
        state.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ConnectionState_IsHealthy_ReturnsTrueWhenConnected()
    {
        // Arrange
        var state = new ConnectionState { Status = ConnectionStatus.Connected };

        // Assert
        state.IsHealthy.Should().BeTrue();
    }

    [Theory]
    [InlineData(ConnectionStatus.Disconnected)]
    [InlineData(ConnectionStatus.Connecting)]
    [InlineData(ConnectionStatus.Reconnecting)]
    public void ConnectionState_IsHealthy_ReturnsFalseWhenNotConnected(ConnectionStatus status)
    {
        // Arrange
        var state = new ConnectionState { Status = status };

        // Assert
        state.IsHealthy.Should().BeFalse();
    }

    [Theory]
    [InlineData(ConnectionStatus.Connected, "success")]
    [InlineData(ConnectionStatus.Connecting, "info")]
    [InlineData(ConnectionStatus.Reconnecting, "warning")]
    [InlineData(ConnectionStatus.Disconnected, "error")]
    public void ConnectionState_StatusColor_ReturnsCorrectColor(ConnectionStatus status, string expectedColor)
    {
        // Arrange
        var state = new ConnectionState { Status = status };

        // Assert
        state.StatusColor.Should().Be(expectedColor);
    }

    [Theory]
    [InlineData(ConnectionStatus.Connected, "SignalCellularAlt")]
    [InlineData(ConnectionStatus.Connecting, "Sync")]
    [InlineData(ConnectionStatus.Reconnecting, "SyncProblem")]
    [InlineData(ConnectionStatus.Disconnected, "SignalCellularOff")]
    public void ConnectionState_StatusIcon_ReturnsCorrectIcon(ConnectionStatus status, string expectedIcon)
    {
        // Arrange
        var state = new ConnectionState { Status = status };

        // Assert
        state.StatusIcon.Should().Be(expectedIcon);
    }

    [Fact]
    public void ConnectionState_StatusText_ReturnsConnected()
    {
        // Arrange
        var state = new ConnectionState { Status = ConnectionStatus.Connected };

        // Assert
        state.StatusText.Should().Be("Connected");
    }

    [Fact]
    public void ConnectionState_StatusText_ReturnsConnecting()
    {
        // Arrange
        var state = new ConnectionState { Status = ConnectionStatus.Connecting };

        // Assert
        state.StatusText.Should().Be("Connecting...");
    }

    [Fact]
    public void ConnectionState_StatusText_ShowsReconnectAttempts()
    {
        // Arrange
        var state = new ConnectionState { Status = ConnectionStatus.Reconnecting, ReconnectAttempts = 3 };

        // Assert
        state.StatusText.Should().Be("Reconnecting (3)...");
    }

    [Fact]
    public void ConnectionState_StatusText_ShowsDisconnected()
    {
        // Arrange
        var state = new ConnectionState { Status = ConnectionStatus.Disconnected };

        // Assert
        state.StatusText.Should().Be("Disconnected");
    }

    [Fact]
    public void ConnectionState_StatusText_ShowsErrorMessage_WhenDisconnectedWithError()
    {
        // Arrange
        var state = new ConnectionState
        {
            Status = ConnectionStatus.Disconnected,
            ErrorMessage = "Connection refused"
        };

        // Assert
        state.StatusText.Should().Be("Connection refused");
    }

    [Fact]
    public void ConnectionState_WithExpression_CreatesNewState()
    {
        // Arrange
        var original = new ConnectionState
        {
            Status = ConnectionStatus.Connected,
            LastConnected = DateTime.UtcNow
        };

        // Act
        var modified = original with { Status = ConnectionStatus.Reconnecting, ReconnectAttempts = 1 };

        // Assert
        modified.Status.Should().Be(ConnectionStatus.Reconnecting);
        modified.ReconnectAttempts.Should().Be(1);
        modified.LastConnected.Should().Be(original.LastConnected);
    }

    #endregion

    #region RegisterHubConnection Tests

    [Fact]
    public void RegisterHubConnection_Constructor_SetsInitialState()
    {
        // Arrange & Act
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Assert
        hubConnection.ConnectionState.Status.Should().Be(ConnectionStatus.Disconnected);
        hubConnection.ConnectionState.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void RegisterHubConnection_Constructor_HandlesTrailingSlash()
    {
        // Arrange & Act - Should not throw regardless of trailing slash
        var hubConnection1 = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);
        var hubConnection2 = new RegisterHubConnection("http://localhost:5000/", _mockLogger.Object);

        // Assert - Both should create successfully
        hubConnection1.Should().NotBeNull();
        hubConnection2.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterHubConnection_SubscribeToRegister_LogsWarning_WhenNotConnected()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Act
        await hubConnection.SubscribeToRegisterAsync("test-register");

        // Assert - Should log warning about not being connected
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not connected")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterHubConnection_StopAsync_DoesNotThrow_WhenNotStarted()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Act & Assert - Should not throw
        await hubConnection.StopAsync();
    }

    [Fact]
    public async Task RegisterHubConnection_DisposeAsync_DoesNotThrow_WhenNotStarted()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Act & Assert - Should not throw
        await hubConnection.DisposeAsync();
    }

    [Fact]
    public async Task RegisterHubConnection_UnsubscribeFromRegister_DoesNotThrow_WhenNotConnected()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Act & Assert - Should not throw, just return silently
        await hubConnection.UnsubscribeFromRegisterAsync("test-register");
    }

    [Fact]
    public void RegisterHubConnection_OnConnectionStateChanged_CanBeSubscribed()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);
        ConnectionState? receivedState = null;

        // Act - Subscribe to the event
        hubConnection.OnConnectionStateChanged += state => receivedState = state;

        // Assert - No exception thrown, event can be subscribed
        // Note: We can't directly trigger the event without connecting,
        // but we verify the event handler can be attached without error
        hubConnection.Should().NotBeNull();
    }

    [Fact]
    public void RegisterHubConnection_Events_CanBeSubscribed()
    {
        // Arrange
        var hubConnection = new RegisterHubConnection("http://localhost:5000", _mockLogger.Object);

        // Act - Subscribe to all events
        hubConnection.OnTransactionConfirmed += (registerId, txId) => Task.CompletedTask;
        hubConnection.OnRegisterCreated += (registerId, name) => Task.CompletedTask;
        hubConnection.OnRegisterDeleted += (registerId) => Task.CompletedTask;
        hubConnection.OnDocketSealed += (registerId, docketId, hash) => Task.CompletedTask;
        hubConnection.OnRegisterHeightUpdated += (registerId, height) => Task.CompletedTask;
        hubConnection.OnConnectionStateChanged += state => { };

        // Assert - No exceptions thrown, events are subscribable
        hubConnection.Should().NotBeNull();
    }

    #endregion

    #region ConnectionStatus Enum Tests

    [Fact]
    public void ConnectionStatus_HasExpectedValues()
    {
        // Assert all expected enum values exist
        Enum.GetValues<ConnectionStatus>().Should().HaveCount(4);
        Enum.IsDefined(ConnectionStatus.Disconnected).Should().BeTrue();
        Enum.IsDefined(ConnectionStatus.Connecting).Should().BeTrue();
        Enum.IsDefined(ConnectionStatus.Connected).Should().BeTrue();
        Enum.IsDefined(ConnectionStatus.Reconnecting).Should().BeTrue();
    }

    #endregion
}
