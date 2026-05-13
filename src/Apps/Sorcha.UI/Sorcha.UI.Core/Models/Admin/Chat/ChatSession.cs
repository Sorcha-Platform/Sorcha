// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Models.Chat;

/// <summary>
/// Client-side representation of a chat session.
/// </summary>
public record ChatSession
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Current blueprint being designed.
    /// </summary>
    public BlueprintModel? Blueprint { get; set; }

    /// <summary>
    /// Conversation history.
    /// </summary>
    public List<ChatMessage> Messages { get; init; } = [];

    /// <summary>
    /// Current validation result.
    /// </summary>
    public ValidationResult? Validation { get; set; }

    /// <summary>
    /// Number of remaining messages before limit.
    /// </summary>
    public int RemainingMessages { get; set; } = 100;

    /// <summary>
    /// Whether the session is connected.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Whether the AI is currently processing.
    /// </summary>
    public bool IsProcessing { get; set; }
}

/// <summary>
/// Blueprint validation result.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Whether the blueprint is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors.
    /// </summary>
    public List<ValidationError> Errors { get; init; } = [];

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public List<ValidationWarning> Warnings { get; init; } = [];
}

/// <summary>
/// A validation error.
/// </summary>
public record ValidationError
{
    /// <summary>
    /// Error code (e.g., MIN_PARTICIPANTS).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Location of the error (e.g., participants, actions).
    /// </summary>
    public string? Location { get; init; }
}

/// <summary>
/// A validation warning.
/// </summary>
public record ValidationWarning
{
    /// <summary>
    /// Warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Location of the warning.
    /// </summary>
    public string? Location { get; init; }
}
