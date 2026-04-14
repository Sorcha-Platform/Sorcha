// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// View model for workflow instance display.
/// </summary>
public record WorkflowInstanceViewModel
{
    [JsonPropertyName("id")]
    public string InstanceId { get; init; } = string.Empty;
    public string BlueprintId { get; init; } = string.Empty;
    public string BlueprintName { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public string? CurrentActionName { get; init; }
    public int CurrentStepNumber { get; init; }
    public int TotalSteps { get; init; }
    public int ParticipantCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// View model for a pending action assigned to the current user.
/// </summary>
public record PendingActionViewModel
{
    public string ActionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string BlueprintId { get; init; } = string.Empty;
    public string RegisterId { get; init; } = string.Empty;
    public string BlueprintName { get; init; } = string.Empty;
    public string ActionName { get; init; } = string.Empty;
    public string InstanceReference { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Priority { get; init; } = "normal";
    public DateTimeOffset AssignedAt { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public System.Text.Json.JsonElement? DataSchema { get; init; }

    /// <summary>
    /// Prepopulated payload seeded by a previous action's Route.OutputMapping
    /// (Feature 104 wave 14a). Null when no seed is attached. For credential
    /// claim actions this carries the minted HAIP offer data.
    /// </summary>
    public JsonObject? PrepopulatedPayload { get; init; }
}

/// <summary>
/// View model for submitting action data.
/// </summary>
public record ActionSubmissionViewModel
{
    public string ActionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public Dictionary<string, object> Data { get; init; } = new();
}

/// <summary>
/// Full request model for executing an action with all required context.
/// </summary>
public record ActionExecuteRequest
{
    public string BlueprintId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public string SenderWallet { get; init; } = string.Empty;
    public string RegisterAddress { get; init; } = string.Empty;
    public Dictionary<string, object> PayloadData { get; init; } = new();
}
