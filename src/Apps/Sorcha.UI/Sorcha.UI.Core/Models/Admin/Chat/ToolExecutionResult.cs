// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Models.Chat;

/// <summary>
/// Client-side representation of a tool execution result.
/// </summary>
public record ToolExecutionResult
{
    /// <summary>
    /// Name of the tool that was executed.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Whether the execution succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// When the tool was executed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Arguments passed to the tool (for display).
    /// </summary>
    public JsonDocument? Arguments { get; init; }
}
