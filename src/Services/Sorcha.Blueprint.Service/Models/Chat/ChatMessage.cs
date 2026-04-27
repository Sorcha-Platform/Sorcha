// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models.Chat;

/// <summary>
/// An individual message in the conversation.
/// </summary>
public record ChatMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Parent session ID.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Who sent this message (User or Assistant).
    /// </summary>
    public required MessageRole Role { get; init; }

    /// <summary>
    /// Message text content (max 10000 characters).
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// AI tool invocations (only for Assistant role).
    /// </summary>
    public List<ToolCall>? ToolCalls { get; init; }

    /// <summary>
    /// Tool execution outcomes.
    /// </summary>
    public List<ToolResult>? ToolResults { get; init; }

    /// <summary>
    /// Binary attachments (images, PDFs) the user dropped onto the chat. Forwarded to the AI
    /// provider as content blocks alongside <see cref="Content"/>. Null on assistant messages
    /// and on user messages with no attachments.
    /// </summary>
    public List<ChatAttachment>? Attachments { get; init; }

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True while AI is still generating this message.
    /// </summary>
    public bool IsStreaming { get; init; }
}
