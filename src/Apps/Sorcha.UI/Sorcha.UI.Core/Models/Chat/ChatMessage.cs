// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Chat;

/// <summary>
/// Client-side representation of a chat message.
/// </summary>
public record ChatMessage
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Who sent this message.
    /// </summary>
    public required MessageRole Role { get; init; }

    /// <summary>
    /// Message text content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Tool executions that occurred during this message.
    /// </summary>
    public List<ToolExecutionResult> ToolResults { get; init; } = [];

    /// <summary>
    /// Binary attachments displayed alongside the text. Set on user messages with file drops.
    /// </summary>
    public List<ChatAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// When the message was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// True while AI is still generating this message.
    /// </summary>
    public bool IsStreaming { get; set; }
}

/// <summary>
/// Role of a message sender.
/// </summary>
public enum MessageRole
{
    /// <summary>Message from the human user.</summary>
    User,

    /// <summary>Message from the AI assistant.</summary>
    Assistant
}
