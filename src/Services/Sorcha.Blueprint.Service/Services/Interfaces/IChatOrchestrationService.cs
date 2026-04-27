// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.Blueprint.Service.Models.Chat;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Orchestrates chat sessions, AI interactions, and tool executions.
/// </summary>
public interface IChatOrchestrationService
{
    /// <summary>
    /// Creates a new chat session for the user.
    /// </summary>
    /// <param name="user">The authenticated user.</param>
    /// <param name="blueprintId">Optional existing blueprint ID to edit.</param>
    /// <returns>The created session.</returns>
    Task<ChatSession> CreateSessionAsync(ClaimsPrincipal user, string? blueprintId = null);

    /// <summary>
    /// Gets an existing session by ID.
    /// </summary>
    Task<ChatSession?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Processes a user message and streams the AI response.
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="message">User's message.</param>
    /// <param name="onChunk">Callback for each text chunk.</param>
    /// <param name="onToolResult">Callback when a tool is executed.</param>
    /// <param name="onBlueprintUpdate">Callback when the blueprint changes.</param>
    /// <param name="attachments">Optional binary attachments (images, PDFs) the user dropped onto the chat.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessMessageAsync(
        string sessionId,
        string message,
        Func<string, Task> onChunk,
        Func<string, ToolResult, Task> onToolResult,
        Func<BlueprintModel, ValidationResultDto, Task> onBlueprintUpdate,
        IReadOnlyList<ChatAttachment>? attachments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current blueprint draft to permanent storage.
    /// </summary>
    Task<BlueprintModel?> SaveBlueprintAsync(string sessionId);

    /// <summary>
    /// Exports the current blueprint draft as JSON or YAML.
    /// </summary>
    Task<string> ExportBlueprintAsync(string sessionId, string format);

    /// <summary>
    /// Ends a chat session.
    /// </summary>
    Task EndSessionAsync(string sessionId);
}

/// <summary>
/// Validation result DTO for client consumption.
/// </summary>
public record ValidationResultDto
{
    /// <summary>Whether the blueprint is valid.</summary>
    public bool IsValid { get; init; }

    /// <summary>Validation errors.</summary>
    public List<ValidationErrorDto> Errors { get; init; } = [];

    /// <summary>Validation warnings.</summary>
    public List<ValidationWarningDto> Warnings { get; init; } = [];
}

/// <summary>Validation error DTO.</summary>
public record ValidationErrorDto(string Code, string Message, string? Location);

/// <summary>Validation warning DTO.</summary>
public record ValidationWarningDto(string Code, string Message, string? Location);
