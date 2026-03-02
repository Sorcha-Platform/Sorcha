// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for EventsHubConnection.
/// Note: Full SignalR integration tests require E2E testing with a live hub.
/// These tests verify constructor behavior, initial state, and safe disposal.
/// </summary>
public class EventsHubConnectionTests
{
    private readonly Mock<ILogger<EventsHubConnection>> _mockLogger;
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<IConfigurationService> _mockConfigService;

    public EventsHubConnectionTests()
    {
        _mockLogger = new Mock<ILogger<EventsHubConnection>>();
        _mockAuthService = new Mock<IAuthenticationService>();
        _mockConfigService = new Mock<IConfigurationService>();
    }

    private EventsHubConnection CreateConnection(string baseUrl = "http://localhost:5000")
    {
        return new EventsHubConnection(
            baseUrl,
            _mockAuthService.Object,
            _mockConfigService.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_SetsInitialState()
    {
        // Arrange & Act
        var connection = CreateConnection();

        // Assert
        connection.ConnectionState.Status.Should().Be(ConnectionStatus.Disconnected);
        connection.ConnectionState.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void Constructor_HandlesTrailingSlash()
    {
        // Arrange & Act - Should not throw regardless of trailing slash
        var connection1 = CreateConnection("http://localhost:5000");
        var connection2 = CreateConnection("http://localhost:5000/");

        // Assert - Both should create successfully
        connection1.Should().NotBeNull();
        connection2.Should().NotBeNull();
    }

    #endregion

    #region Event Subscription Tests

    [Fact]
    public void OnEventReceived_CanBeSubscribed()
    {
        // Arrange
        var connection = CreateConnection();

        // Act - Subscribe to event
        connection.OnEventReceived += dto => { };

        // Assert - No exception thrown
        connection.Should().NotBeNull();
    }

    [Fact]
    public void OnUnreadCountUpdated_CanBeSubscribed()
    {
        // Arrange
        var connection = CreateConnection();

        // Act - Subscribe to event
        connection.OnUnreadCountUpdated += count => { };

        // Assert - No exception thrown
        connection.Should().NotBeNull();
    }

    [Fact]
    public void OnConnectionStateChanged_CanBeSubscribed()
    {
        // Arrange
        var connection = CreateConnection();
        ConnectionState? receivedState = null;

        // Act - Subscribe to event
        connection.OnConnectionStateChanged += state => receivedState = state;

        // Assert - No exception thrown, event can be subscribed
        connection.Should().NotBeNull();
    }

    [Fact]
    public void AllEvents_CanBeSubscribedSimultaneously()
    {
        // Arrange
        var connection = CreateConnection();

        // Act - Subscribe to all events
        connection.OnEventReceived += dto => { };
        connection.OnUnreadCountUpdated += count => { };
        connection.OnConnectionStateChanged += state => { };

        // Assert - No exceptions thrown
        connection.Should().NotBeNull();
    }

    #endregion

    #region Disconnect Tests

    [Fact]
    public async Task DisconnectAsync_DoesNotThrow_WhenNotConnected()
    {
        // Arrange
        var connection = CreateConnection();

        // Act & Assert - Should not throw when no connection exists
        await connection.DisconnectAsync();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_DoesNotThrow_WhenNotConnected()
    {
        // Arrange
        var connection = CreateConnection();

        // Act & Assert - Should not throw
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var connection = CreateConnection();

        // Act & Assert - Should not throw on multiple dispose calls
        await connection.DisposeAsync();
        await connection.DisposeAsync();
    }

    #endregion
}
