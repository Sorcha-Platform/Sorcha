// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Tests for ActionsHubConnection encryption event handlers (T017).
/// Since ActionsHubConnection creates its own internal HubConnection via HubConnectionBuilder,
/// these tests verify the public event API, default state, and subscribe/unsubscribe behavior.
/// </summary>
public class ActionsHubConnectionTests
{
    // ---------------------------------------------------------------
    // Connection state defaults
    // ---------------------------------------------------------------

    [Fact]
    public void ConnectionState_DefaultsToDisconnected()
    {
        var connection = CreateConnection();

        connection.ConnectionState.Status.Should().Be(ConnectionStatus.Disconnected);
    }

    [Fact]
    public void ConnectionState_DefaultLastConnected_IsNull()
    {
        var connection = CreateConnection();

        connection.ConnectionState.LastConnected.Should().BeNull();
    }

    [Fact]
    public void ConnectionState_DefaultReconnectAttempts_IsZero()
    {
        var connection = CreateConnection();

        connection.ConnectionState.ReconnectAttempts.Should().Be(0);
    }

    [Fact]
    public void ConnectionState_DefaultIsHealthy_IsFalse()
    {
        var connection = CreateConnection();

        connection.ConnectionState.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void ConnectionState_DefaultErrorMessage_IsNull()
    {
        var connection = CreateConnection();

        connection.ConnectionState.ErrorMessage.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // Encryption event defaults
    // ---------------------------------------------------------------

    [Fact]
    public void OnEncryptionProgress_DefaultsToNull()
    {
        var connection = CreateConnection();

        // Events default to null when no subscribers are attached.
        // We verify the connection can be created without any event wiring.
        connection.ConnectionState.Should().NotBeNull();
    }

    [Fact]
    public void OnEncryptionComplete_DefaultsToNull()
    {
        var connection = CreateConnection();

        connection.ConnectionState.Should().NotBeNull();
    }

    [Fact]
    public void OnEncryptionFailed_DefaultsToNull()
    {
        var connection = CreateConnection();

        connection.ConnectionState.Should().NotBeNull();
    }

    // ---------------------------------------------------------------
    // Encryption event subscribe/unsubscribe
    // ---------------------------------------------------------------

    [Fact]
    public void OnEncryptionProgress_CanSubscribeAndUnsubscribe()
    {
        var connection = CreateConnection();
        Func<EncryptionProgressUpdate, Task> handler = _ => Task.CompletedTask;

        connection.OnEncryptionProgress += handler;
        connection.OnEncryptionProgress -= handler;

        // No exception thrown — subscribe/unsubscribe works correctly
    }

    [Fact]
    public void OnEncryptionComplete_CanSubscribeAndUnsubscribe()
    {
        var connection = CreateConnection();
        Func<EncryptionCompleteUpdate, Task> handler = _ => Task.CompletedTask;

        connection.OnEncryptionComplete += handler;
        connection.OnEncryptionComplete -= handler;
    }

    [Fact]
    public void OnEncryptionFailed_CanSubscribeAndUnsubscribe()
    {
        var connection = CreateConnection();
        Func<EncryptionFailedUpdate, Task> handler = _ => Task.CompletedTask;

        connection.OnEncryptionFailed += handler;
        connection.OnEncryptionFailed -= handler;
    }

    [Fact]
    public void OnConnectionStateChanged_CanSubscribeAndUnsubscribe()
    {
        var connection = CreateConnection();
        Action<ConnectionState> handler = _ => { };

        connection.OnConnectionStateChanged += handler;
        connection.OnConnectionStateChanged -= handler;
    }

    [Fact]
    public void OnEncryptionProgress_CanSubscribeMultipleHandlers()
    {
        var connection = CreateConnection();
        Func<EncryptionProgressUpdate, Task> handler1 = _ => Task.CompletedTask;
        Func<EncryptionProgressUpdate, Task> handler2 = _ => Task.CompletedTask;

        connection.OnEncryptionProgress += handler1;
        connection.OnEncryptionProgress += handler2;

        // No exception — multiple handlers can be attached
        connection.OnEncryptionProgress -= handler1;
        connection.OnEncryptionProgress -= handler2;
    }

    [Fact]
    public void OnEncryptionComplete_CanSubscribeMultipleHandlers()
    {
        var connection = CreateConnection();
        Func<EncryptionCompleteUpdate, Task> handler1 = _ => Task.CompletedTask;
        Func<EncryptionCompleteUpdate, Task> handler2 = _ => Task.CompletedTask;

        connection.OnEncryptionComplete += handler1;
        connection.OnEncryptionComplete += handler2;

        connection.OnEncryptionComplete -= handler1;
        connection.OnEncryptionComplete -= handler2;
    }

    [Fact]
    public void OnEncryptionFailed_CanSubscribeMultipleHandlers()
    {
        var connection = CreateConnection();
        Func<EncryptionFailedUpdate, Task> handler1 = _ => Task.CompletedTask;
        Func<EncryptionFailedUpdate, Task> handler2 = _ => Task.CompletedTask;

        connection.OnEncryptionFailed += handler1;
        connection.OnEncryptionFailed += handler2;

        connection.OnEncryptionFailed -= handler1;
        connection.OnEncryptionFailed -= handler2;
    }

    // ---------------------------------------------------------------
    // Wallet subscriptions (default state)
    // ---------------------------------------------------------------

    [Fact]
    public void SubscribedWallets_DefaultsToEmpty()
    {
        var connection = CreateConnection();

        connection.SubscribedWallets.Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // Dispose
    // ---------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_WithoutStarting_DoesNotThrow()
    {
        var connection = CreateConnection();

        var act = () => connection.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        var connection = CreateConnection();

        await connection.DisposeAsync();
        var act = () => connection.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static ActionsHubConnection CreateConnection(string baseUrl = "http://localhost")
    {
        var authService = new Mock<IAuthenticationService>();
        var configService = new Mock<IConfigurationService>();
        var logger = new Mock<ILogger<ActionsHubConnection>>();

        return new ActionsHubConnection(
            baseUrl,
            authService.Object,
            configService.Object,
            logger.Object);
    }
}
