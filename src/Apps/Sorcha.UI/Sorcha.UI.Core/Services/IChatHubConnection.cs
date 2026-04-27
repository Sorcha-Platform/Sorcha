// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using Sorcha.UI.Core.Models.Chat;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Interface for SignalR connection to the Chat Hub.
/// </summary>
public interface IChatHubConnection : IAsyncDisposable
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ChatConnectionState State { get; }

    /// <summary>
    /// Event raised when a text chunk is received from the AI.
    /// </summary>
    event Action<string>? OnChunkReceived;

    /// <summary>
    /// Event raised when a tool execution completes.
    /// </summary>
    event Action<string, bool, string?>? OnToolExecuted;

    /// <summary>
    /// Event raised when the blueprint is updated.
    /// </summary>
    event Action<BlueprintModel, ValidationResult>? OnBlueprintUpdated;

    /// <summary>
    /// Event raised when a message is complete.
    /// </summary>
    event Action<string>? OnMessageComplete;

    /// <summary>
    /// Event raised when a session error occurs.
    /// </summary>
    event Action<string, string>? OnSessionError;

    /// <summary>
    /// Event raised when approaching the message limit.
    /// </summary>
    event Action<int>? OnMessageLimitWarning;

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    event Action<ChatConnectionState>? OnStateChanged;

    /// <summary>
    /// Event raised when a session is started (includes loaded blueprint if editing).
    /// Parameters: sessionId, blueprint (null if new), messageCount
    /// </summary>
    event Action<string, BlueprintModel?, int>? OnSessionStarted;

    /// <summary>
    /// Connects to the Chat Hub.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the Chat Hub.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Starts a new chat session or resumes an existing one.
    /// </summary>
    /// <param name="existingBlueprintId">Optional blueprint ID to edit.</param>
    /// <returns>Session ID.</returns>
    Task<string> StartSessionAsync(string? existingBlueprintId = null);

    /// <summary>
    /// Sends a user message to the AI, optionally with image / PDF attachments.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <param name="message">User's message (may be empty when <paramref name="attachments"/> is non-empty).</param>
    /// <param name="attachments">Optional binary attachments — base64-encoded, mirrors the server record shape.</param>
    Task SendMessageAsync(string sessionId, string message, IReadOnlyList<ChatAttachment>? attachments = null);

    /// <summary>
    /// Cancels the current AI generation.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    Task CancelGenerationAsync(string sessionId);

    /// <summary>
    /// Saves the blueprint to permanent storage.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <returns>Saved blueprint ID.</returns>
    Task<string> SaveBlueprintAsync(string sessionId);

    /// <summary>
    /// Exports the blueprint as JSON or YAML.
    /// </summary>
    /// <param name="sessionId">Active session ID.</param>
    /// <param name="format">"json" or "yaml".</param>
    /// <returns>Serialized blueprint content.</returns>
    Task<string> ExportBlueprintAsync(string sessionId, string format);

    /// <summary>
    /// Ends the chat session.
    /// </summary>
    /// <param name="sessionId">Session ID to end.</param>
    Task EndSessionAsync(string sessionId);
}

/// <summary>
/// Connection state for the Chat Hub.
/// </summary>
public enum ChatConnectionState
{
    /// <summary>Not connected.</summary>
    Disconnected,

    /// <summary>Attempting to connect.</summary>
    Connecting,

    /// <summary>Connected and ready.</summary>
    Connected,

    /// <summary>Reconnecting after connection loss.</summary>
    Reconnecting
}
