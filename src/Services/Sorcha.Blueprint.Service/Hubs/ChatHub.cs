// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// SignalR hub for AI-assisted blueprint design chat.
/// </summary>
/// <remarks>
/// Connection URL: /hubs/chat
/// Authentication: JWT token via query parameter ?access_token={jwt}
///
/// Client-to-Server Methods:
/// - StartSession(existingBlueprintId?): Start or resume a chat session
/// - SendMessage(sessionId, message): Send a user message
/// - CancelGeneration(sessionId): Cancel current AI generation
/// - SaveBlueprint(sessionId): Save the blueprint to storage
/// - ExportBlueprint(sessionId, format): Export as JSON or YAML
/// - EndSession(sessionId): End the chat session
///
/// Server-to-Client Events:
/// - SessionStarted(sessionId, blueprint?, messageCount): Session created/resumed
/// - ReceiveChunk(chunk): Streaming text from AI
/// - ToolExecuting(toolName, arguments): AI is executing a tool
/// - ToolExecuted(toolName, success, error?): Tool execution completed
/// - BlueprintUpdated(blueprint, validation): Blueprint changed
/// - MessageComplete(messageId): AI response finished
/// - SessionError(errorCode, message): Error occurred
/// - MessageLimitWarning(remaining): Approaching message limit
/// </remarks>
[Authorize]
public class ChatHub : Hub
{
    private readonly IChatOrchestrationService _orchestration;
    private readonly ILogger<ChatHub> _logger;
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public ChatHub(
        IChatOrchestrationService orchestration,
        ILogger<ChatHub> logger)
    {
        _orchestration = orchestration;
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        var userIdentifier = Context.UserIdentifier;

        _logger.LogInformation(
            "Client connected to ChatHub. ConnectionId: {ConnectionId}, User: {User}",
            connectionId,
            userIdentifier ?? "anonymous");

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        // Cancel any ongoing generation
        if (_cancellationTokens.TryGetValue(connectionId, out var cts))
        {
            cts.Cancel();
            _cancellationTokens.TryRemove(connectionId, out _);
        }

        if (exception != null)
        {
            _logger.LogWarning(
                exception,
                "Client disconnected from ChatHub with error. ConnectionId: {ConnectionId}",
                connectionId);
        }
        else
        {
            _logger.LogInformation(
                "Client disconnected from ChatHub. ConnectionId: {ConnectionId}",
                connectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Starts a new chat session or resumes an existing one.
    /// </summary>
    /// <param name="existingBlueprintId">Optional blueprint ID to load for editing.</param>
    /// <returns>Session ID.</returns>
    public async Task<string> StartSession(string? existingBlueprintId = null)
    {
        try
        {
            var user = Context.User ?? throw new HubException("User not authenticated");
            var session = await _orchestration.CreateSessionAsync(user, existingBlueprintId);

            // Notify client of session start
            await Clients.Caller.SendAsync("SessionStarted",
                session.Id,
                session.BlueprintDraft,
                session.Messages.Count);

            // Warn if approaching message limit
            if (session.IsApproachingMessageLimit)
            {
                await Clients.Caller.SendAsync("MessageLimitWarning", session.RemainingMessages);
            }

            _logger.LogInformation("Started session {SessionId} for connection {ConnectionId}",
                session.Id, Context.ConnectionId);

            return session.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting session");
            await Clients.Caller.SendAsync("SessionError", "SESSION_START_FAILED", ex.Message);
            throw new HubException(ex.Message);
        }
    }

    /// <summary>
    /// Sends a user message to the AI assistant.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <param name="message">User's natural language input (max 10000 chars).</param>
    public async Task SendMessage(string sessionId, string message)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new HubException("Session ID is required");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new HubException("Message cannot be empty");
        }

        if (message.Length > 10000)
        {
            throw new HubException("Message too long (max 10000 characters)");
        }

        // Create cancellation token for this message
        var cts = new CancellationTokenSource();
        _cancellationTokens[Context.ConnectionId] = cts;

        try
        {
            var messageId = Guid.NewGuid().ToString();

            await _orchestration.ProcessMessageAsync(
                sessionId,
                message,
                onChunk: async chunk =>
                {
                    await Clients.Caller.SendAsync("ReceiveChunk", chunk);
                },
                onToolResult: async (toolName, result) =>
                {
                    // Notify tool execution
                    await Clients.Caller.SendAsync("ToolExecuted",
                        toolName,
                        result.Success,
                        result.Error);
                },
                onBlueprintUpdate: async (blueprint, validation) =>
                {
                    await Clients.Caller.SendAsync("BlueprintUpdated", blueprint, validation);
                },
                cancellationToken: cts.Token);

            // Message complete
            await Clients.Caller.SendAsync("MessageComplete", messageId);

            // Check message limit after processing
            var session = await _orchestration.GetSessionAsync(sessionId);
            if (session?.IsApproachingMessageLimit == true)
            {
                await Clients.Caller.SendAsync("MessageLimitWarning", session.RemainingMessages);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message generation cancelled for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", "GENERATION_CANCELLED", "Generation was cancelled");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired"))
        {
            await Clients.Caller.SendAsync("SessionError", "SESSION_EXPIRED", ex.Message);
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("limit"))
        {
            await Clients.Caller.SendAsync("SessionError", "MESSAGE_LIMIT", ex.Message);
            throw new HubException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", "AI_UNAVAILABLE", "AI service unavailable. Please try again.");
            throw new HubException("Failed to process message");
        }
        finally
        {
            _cancellationTokens.TryRemove(Context.ConnectionId, out _);
        }
    }

    /// <summary>
    /// Cancels the current AI response generation.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    public Task CancelGeneration(string sessionId)
    {
        if (_cancellationTokens.TryGetValue(Context.ConnectionId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancelled generation for session {SessionId}", sessionId);
        }
        else
        {
            throw new HubException("No generation in progress");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves the current draft blueprint to permanent storage.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <returns>Saved blueprint ID.</returns>
    public async Task<string> SaveBlueprint(string sessionId)
    {
        try
        {
            var saved = await _orchestration.SaveBlueprintAsync(sessionId)
                ?? throw new HubException("Failed to save blueprint");

            _logger.LogInformation("Saved blueprint {BlueprintId} from session {SessionId}",
                saved.Id, sessionId);

            return saved.Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error saving blueprint for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", "SAVE_FAILED", ex.Message);
            throw new HubException(ex.Message);
        }
    }

    /// <summary>
    /// Exports the current draft as JSON or YAML.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <param name="format">"json" or "yaml".</param>
    /// <returns>Serialized blueprint content.</returns>
    public async Task<string> ExportBlueprint(string sessionId, string format)
    {
        try
        {
            return await _orchestration.ExportBlueprintAsync(sessionId, format);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting blueprint for session {SessionId}", sessionId);
            throw new HubException(ex.Message);
        }
    }

    /// <summary>
    /// Explicitly ends a chat session.
    /// </summary>
    /// <param name="sessionId">Session ID to end.</param>
    public async Task EndSession(string sessionId)
    {
        try
        {
            // Cancel any ongoing generation
            if (_cancellationTokens.TryGetValue(Context.ConnectionId, out var cts))
            {
                cts.Cancel();
                _cancellationTokens.TryRemove(Context.ConnectionId, out _);
            }

            await _orchestration.EndSessionAsync(sessionId);

            _logger.LogInformation("Ended session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending session {SessionId}", sessionId);
            throw new HubException(ex.Message);
        }
    }
}
