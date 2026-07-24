// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Blueprint summary for list operations.
/// </summary>
public class BlueprintSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("participantCount")]
    public int ParticipantCount { get; set; }

    [JsonPropertyName("actionCount")]
    public int ActionCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Full blueprint detail.
/// </summary>
public class BlueprintDetail
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("participantCount")]
    public int ParticipantCount { get; set; }

    [JsonPropertyName("actionCount")]
    public int ActionCount { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("participants")]
    public List<BlueprintParticipant> Participants { get; set; } = new();

    [JsonPropertyName("actions")]
    public List<BlueprintAction> Actions { get; set; } = new();
}

/// <summary>
/// A participant defined in a blueprint.
/// </summary>
public class BlueprintParticipant
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// An action defined in a blueprint.
/// </summary>
public class BlueprintAction
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("participant")]
    public string Participant { get; set; } = string.Empty;
}

/// <summary>
/// Request to create a blueprint.
/// </summary>
public class CreateBlueprintRequest
{
    [JsonPropertyName("blueprintJson")]
    public string BlueprintJson { get; set; } = string.Empty;
}

/// <summary>
/// Request to publish a blueprint to a register.
/// </summary>
/// <remarks>
/// Mirrors the server's <c>PublishRequest</c> for <c>POST /api/blueprints/{id}/publish</c>. The
/// blueprint id is a route parameter, so this body carries only <c>registerId</c> and an optional
/// <c>override</c>. The CLI previously sent a <c>blueprintId</c> the server never reads and had no
/// way to pass the rehearsal-gate override, so an operator could not force-publish from the CLI.
/// </remarks>
public class PublishBlueprintRequest
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>Optional rehearsal-gate override. Null means a normal, gated publish.</summary>
    [JsonPropertyName("override")]
    public PublishBlueprintOverride? Override { get; set; }
}

/// <summary>
/// The override that lets an authorised caller publish past the rehearsal soft-gate.
/// </summary>
public class PublishBlueprintOverride
{
    /// <summary>Must be true to actually override.</summary>
    [JsonPropertyName("confirm")]
    public bool Confirm { get; set; }

    /// <summary>Optional reason, recorded in the publish-override audit.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Response after publishing a blueprint.
/// </summary>
public class PublishBlueprintResponse
{
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Blueprint version information.
/// </summary>
public class BlueprintVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("changeDescription")]
    public string ChangeDescription { get; set; } = string.Empty;
}

/// <summary>
/// Blueprint instance (running workflow).
/// </summary>
public class BlueprintInstance
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("currentActionId")]
    public int? CurrentActionId { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }
}
