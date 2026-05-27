// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Service.Models.Chat;

/// <summary>
/// Base class for AI streaming events.
/// </summary>
public abstract record AIStreamEvent;

/// <summary>
/// A chunk of text from the AI response.
/// </summary>
/// <param name="Text">The text.</param>
public record TextChunk(string Text) : AIStreamEvent;

/// <summary>
/// The AI wants to use a tool.
/// </summary>
/// <param name="Id">Unique identifier for the resource.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Arguments">The arguments.</param>
public record ToolUse(string Id, string Name, JsonDocument Arguments) : AIStreamEvent;

/// <summary>
/// The AI has finished generating the response.
/// </summary>
/// <param name="StopReason">The stop reason.</param>
public record StreamEnd(string? StopReason = null) : AIStreamEvent;

/// <summary>
/// An error occurred during streaming.
/// </summary>
/// <param name="Message">Human-readable message.</param>
/// <param name="IsRetryable">Indicates whether retryable.</param>
public record StreamError(string Message, bool IsRetryable = false) : AIStreamEvent;
