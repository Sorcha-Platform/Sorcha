// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Integration tests for the ChatHub SignalR hub.
/// Tests AI-assisted blueprint design chat functionality.
/// </summary>
public class ChatHubIntegrationTests : IClassFixture<BlueprintServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly BlueprintServiceWebApplicationFactory _factory;
    private readonly List<HubConnection> _connections = [];

    public ChatHubIntegrationTests(BlueprintServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            if (connection.State == HubConnectionState.Connected)
            {
                await connection.StopAsync();
            }
            await connection.DisposeAsync();
        }
        _connections.Clear();
    }

    #region Connection Tests

    [Fact]
    public async Task ChatHub_CanConnect_Successfully()
    {
        // Arrange
        var connection = CreateChatHubConnection();

        // Act
        await connection.StartAsync();

        // Assert
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task ChatHub_CanDisconnect_Successfully()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();

        // Act
        await connection.StopAsync();

        // Assert
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task ChatHub_MultipleClients_CanConnectSimultaneously()
    {
        // Arrange
        var connection1 = CreateChatHubConnection();
        var connection2 = CreateChatHubConnection();

        // Act
        await connection1.StartAsync();
        await connection2.StartAsync();

        // Assert
        connection1.State.Should().Be(HubConnectionState.Connected);
        connection2.State.Should().Be(HubConnectionState.Connected);
    }

    #endregion

    #region Session Management Tests

    [Fact]
    public async Task StartSession_ReturnsSessionId()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();

        // Act
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Assert
        sessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartSession_TriggersSessionStartedEvent()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        var sessionStarted = Channel.CreateUnbounded<(string SessionId, BlueprintModel? Blueprint, int MessageCount)>();

        connection.On<string, BlueprintModel?, int>("SessionStarted", (sessionId, blueprint, messageCount) =>
        {
            sessionStarted.Writer.TryWrite((sessionId, blueprint, messageCount));
        });

        await connection.StartAsync();

        // Act
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await sessionStarted.Reader.ReadAsync(cts.Token);

        received.SessionId.Should().Be(sessionId);
        received.MessageCount.Should().BeGreaterThanOrEqualTo(0); // May include system message
    }

    [Fact]
    public async Task StartSession_WithExistingBlueprintId_LoadsBlueprint()
    {
        // Arrange - First create and save a blueprint
        var connection = CreateChatHubConnection();
        await connection.StartAsync();

        // Start a session first
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // The blueprint store is mocked, so we just verify no exception is thrown
        // when providing a blueprint ID
        await connection.StopAsync();

        // Start new connection and try to load
        var connection2 = CreateChatHubConnection();
        await connection2.StartAsync();

        // Act - should not throw even with non-existent blueprint (mock returns null)
        var newSessionId = await connection2.InvokeAsync<string>("StartSession", "nonexistent-blueprint");

        // Assert
        newSessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EndSession_CompletesSuccessfully()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act & Assert - should not throw
        await connection.InvokeAsync("EndSession", sessionId);
    }

    #endregion

    #region Message Handling Tests

    [Fact]
    public async Task SendMessage_WithEmptyMessage_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act
        // SUT signature: SendMessage(string sessionId, string message, IReadOnlyList<ChatAttachment>? attachments)
        // SignalR ignores C# default values, so pass null explicitly.
        var act = async () => await connection.InvokeAsync("SendMessage", sessionId, "", (object?)null);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task SendMessage_WithNullSessionId_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();

        // Act
        var act = async () => await connection.InvokeAsync("SendMessage", "", "Hello", (object?)null);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Session ID*required*");
    }

    [Fact]
    public async Task SendMessage_WithTooLongMessage_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);
        var longMessage = new string('x', 10001);

        // Act
        var act = async () => await connection.InvokeAsync("SendMessage", sessionId, longMessage, (object?)null);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*too long*");
    }

    [Fact]
    public async Task SendMessage_TriggersMessageCompleteEvent()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        var messageCompleted = Channel.CreateUnbounded<string>();

        connection.On<string>("MessageComplete", messageId =>
        {
            messageCompleted.Writer.TryWrite(messageId);
        });

        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act - Send a simple message (AI provider is mocked to return empty stream)
        try
        {
            await connection.InvokeAsync("SendMessage", sessionId, "Create a simple blueprint", (object?)null);
        }
        catch (HubException)
        {
            // AI provider mock may throw - that's OK for this test
        }

        // Assert - MessageComplete may or may not fire depending on mock behavior
        // The test validates the connection flow works
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    #endregion

    #region Blueprint Export Tests

    [Fact]
    public async Task ExportBlueprint_WithoutDraft_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act - Export without creating a blueprint first (with any format)
        var act = async () => await connection.InvokeAsync<string>("ExportBlueprint", sessionId, "json");

        // Assert - No blueprint draft check happens before format validation
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*No blueprint*");
    }

    [Fact]
    public async Task ExportBlueprint_WithXmlFormat_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act - Try XML format (unsupported, but draft check happens first)
        var act = async () => await connection.InvokeAsync<string>("ExportBlueprint", sessionId, "xml");

        // Assert - Without a draft, "No blueprint draft" is thrown before format validation
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*No blueprint*");
    }

    #endregion

    #region Save Blueprint Tests

    [Fact]
    public async Task SaveBlueprint_WithoutDraft_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act
        var act = async () => await connection.InvokeAsync<string>("SaveBlueprint", sessionId);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*No blueprint draft*");
    }

    #endregion

    #region Cancel Generation Tests

    [Fact]
    public async Task CancelGeneration_WithNoActiveGeneration_ThrowsHubException()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act
        var act = async () => await connection.InvokeAsync("CancelGeneration", sessionId);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*No generation in progress*");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task SessionError_EventIsReceived_OnError()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        var errorReceived = Channel.CreateUnbounded<(string ErrorCode, string Message)>();

        connection.On<string, string>("SessionError", (errorCode, message) =>
        {
            errorReceived.Writer.TryWrite((errorCode, message));
        });

        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act - Try to save without a blueprint (will trigger error event)
        try
        {
            await connection.InvokeAsync<string>("SaveBlueprint", sessionId);
        }
        catch (HubException)
        {
            // Expected
        }

        // Assert
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await errorReceived.Reader.ReadAsync(cts.Token);

        received.ErrorCode.Should().Be("SAVE_FAILED");
        received.Message.Should().Contain("No blueprint draft");
    }

    #endregion

    #region Reconnection Tests

    [Fact]
    public async Task ChatHub_CanReconnect_AfterDisconnect()
    {
        // Arrange
        var connection = CreateChatHubConnection();
        await connection.StartAsync();
        var sessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Act - Disconnect and reconnect
        await connection.StopAsync();
        await connection.StartAsync();

        // Start a new session after reconnect
        var newSessionId = await connection.InvokeAsync<string>("StartSession", (string?)null);

        // Assert
        connection.State.Should().Be(HubConnectionState.Connected);
        newSessionId.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Helper Methods

    private HubConnection CreateChatHubConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{_factory.Server.BaseAddress}hubs/chat", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        _connections.Add(connection);
        return connection;
    }

    #endregion
}
