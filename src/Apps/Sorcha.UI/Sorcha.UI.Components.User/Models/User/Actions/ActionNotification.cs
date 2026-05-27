// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Actions;

/// <summary>
/// Thin signal notification received from SignalR.
/// Contains only signal type and instance identifier — UI pulls details
/// through authenticated REST endpoints after receiving the signal.
/// </summary>
public record SignalNotification
{
    /// <summary>
    /// Signal type: "action-available", "action-rejected", "workflow-completed", "inbound-action".
    /// </summary>
    public string SignalType { get; init; } = string.Empty;

    /// <summary>
    /// Blueprint instance identifier.
    /// </summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>
    /// Optional correlation identifier for pull-back requests.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// Optional action identifier carried on action-scoped signals.
    /// </summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// UTC timestamp of signal creation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
