// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Represents an event received from a SignalR hub.
/// </summary>
public class EventStreamMessage
{
    /// <summary>
    /// The type of event (e.g., TransactionConfirmed, DocketSealed).
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The register ID associated with the event, if applicable.
    /// </summary>
    public string? RegisterId { get; set; }

    /// <summary>
    /// UTC timestamp when the event was received.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The event payload data.
    /// </summary>
    public object? Data { get; set; }
}
