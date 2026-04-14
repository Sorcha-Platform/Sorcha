// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Lightweight projection of a pending action with blueprint context.
/// Returned by pending action query endpoints.
/// </summary>
public class PendingActionSummary
{
    /// <summary>Blueprint instance ID.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Action ID within the blueprint.</summary>
    public int ActionId { get; init; }

    /// <summary>Human-readable action title.</summary>
    public string ActionTitle { get; init; } = string.Empty;

    /// <summary>Blueprint ID.</summary>
    public required string BlueprintId { get; init; }

    /// <summary>Blueprint display name.</summary>
    public string BlueprintTitle { get; init; } = string.Empty;

    /// <summary>Human-readable instance reference (e.g., "CP-RIV-14W-a7k3").</summary>
    public string InstanceReference { get; init; } = string.Empty;

    /// <summary>Wallet address of the previous action submitter.</summary>
    public string SenderAddress { get; init; } = string.Empty;

    /// <summary>Resolved participant display name.</summary>
    public string SenderDisplayName { get; init; } = string.Empty;

    /// <summary>Rendered notification summary.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Urgency level: normal, warning, or urgent.</summary>
    public string Urgency { get; init; } = "normal";

    /// <summary>Deadline timestamp, if configured.</summary>
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>Register containing the instance.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Triggering transaction ID.</summary>
    public string TransactionId { get; init; } = string.Empty;

    /// <summary>Deep link path to the action page.</summary>
    public string? NavigationPath { get; init; }

    /// <summary>When the notification was created.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>
    /// Prepopulated payload seeded for this action by a previous action's
    /// <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/>. Null when
    /// no seed data is attached. Consumers (e.g. MyActions.razor) pass this
    /// to the form renderer as initial state. Feature 104 wave 14a.
    /// </summary>
    public JsonObject? PrepopulatedPayload { get; init; }
}
