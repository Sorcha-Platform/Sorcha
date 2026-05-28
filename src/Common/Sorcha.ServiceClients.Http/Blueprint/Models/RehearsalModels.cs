// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Blueprint.Models;

// =============================================================================
// Feature 142 — Blueprint Design Lifecycle Overhaul.
// Service-client DTOs for the new rehearsal / publish-override / amend surface.
// Wire shapes mirror specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml.
// Implementations of the corresponding IBlueprintServiceClient methods land in US2/US3.
// =============================================================================

/// <summary>
/// Rehearsal execution mode (Feature 142). A <see cref="Full"/> rehearsal runs the real
/// execution pipeline against the org sandbox register; a <see cref="DryRun"/> is handled
/// entirely client-side (in-WASM engine) and never calls the rehearsal HTTP surface.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RehearsalMode>))]
public enum RehearsalMode
{
    /// <summary>Client-side dry-run (no HTTP). Present on read responses for completeness.</summary>
    DryRun,

    /// <summary>Full rehearsal against the org sandbox register.</summary>
    Full
}

/// <summary>
/// Terminal/transient outcome of a rehearsal (Feature 142).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RehearsalOutcome>))]
public enum RehearsalOutcome
{
    /// <summary>The rehearsal is still being walked through.</summary>
    InProgress,

    /// <summary>Reached a successful terminal state; records a RehearsalPass that unlocks Go live.</summary>
    Passed,

    /// <summary>Reset/discarded before completion.</summary>
    Abandoned,

    /// <summary>The rehearsal hit an unrecoverable error.</summary>
    Failed
}

/// <summary>
/// Walk-through status of a single rehearsal step (Feature 142).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RehearsalStepStatus>))]
public enum RehearsalStepStatus
{
    /// <summary>Not yet reached.</summary>
    Pending,

    /// <summary>The step awaiting submission right now.</summary>
    Current,

    /// <summary>Submitted and applied.</summary>
    Done
}

/// <summary>
/// Classification of a rehearsal log entry (Feature 142).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RehearsalEventKind>))]
public enum RehearsalEventKind
{
    /// <summary>Informational message.</summary>
    Info,

    /// <summary>A transaction was sealed onto the sandbox register.</summary>
    Sealed,

    /// <summary>A gate (validation / governance) outcome.</summary>
    Gate,

    /// <summary>An action was routed to the next participant.</summary>
    Routed,

    /// <summary>Disclosed data / credential was delivered.</summary>
    Delivered,

    /// <summary>An error occurred during the step.</summary>
    Error
}

/// <summary>
/// A single step in the rehearsal walk-through (Feature 142).
/// </summary>
public sealed record RehearsalStep
{
    /// <summary>The blueprint action this step exercises.</summary>
    [JsonPropertyName("actionId")]
    public int ActionId { get; init; }

    /// <summary>The participant role acting on this step.</summary>
    [JsonPropertyName("actingRole")]
    public string ActingRole { get; init; } = string.Empty;

    /// <summary>Walk-through status of this step.</summary>
    [JsonPropertyName("status")]
    public RehearsalStepStatus Status { get; init; }
}

/// <summary>
/// A single entry in the rehearsal activity log (Feature 142).
/// </summary>
public sealed record RehearsalEvent
{
    /// <summary>When the event was recorded (UTC).</summary>
    [JsonPropertyName("at")]
    public DateTimeOffset At { get; init; }

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Classification of the event.</summary>
    [JsonPropertyName("kind")]
    public RehearsalEventKind Kind { get; init; }
}

/// <summary>
/// The state of a rehearsal: status, current acting role, walk-through steps, and log
/// (Feature 142). Returned by start / get / role-switch / step-submit.
/// </summary>
public sealed record Rehearsal
{
    /// <summary>Unique rehearsal identifier.</summary>
    [JsonPropertyName("rehearsalId")]
    public Guid RehearsalId { get; init; }

    /// <summary>The blueprint being rehearsed.</summary>
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Executable-definition hash of the rehearsed version (matched against RehearsalPass at publish).</summary>
    [JsonPropertyName("execDefHash")]
    public string ExecDefHash { get; init; } = string.Empty;

    /// <summary>Execution mode.</summary>
    [JsonPropertyName("mode")]
    public RehearsalMode Mode { get; init; }

    /// <summary>The per-org sandbox register the full rehearsal runs on; null for dry-run.</summary>
    [JsonPropertyName("sandboxRegisterId")]
    public string? SandboxRegisterId { get; init; }

    /// <summary>The participant role currently acting.</summary>
    [JsonPropertyName("currentActingRole")]
    public string CurrentActingRole { get; init; } = string.Empty;

    /// <summary>The rehearsal outcome.</summary>
    [JsonPropertyName("outcome")]
    public RehearsalOutcome Outcome { get; init; }

    /// <summary>The walk-through steps in order.</summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<RehearsalStep> Steps { get; init; } = [];

    /// <summary>The activity log, oldest first.</summary>
    [JsonPropertyName("log")]
    public IReadOnlyList<RehearsalEvent> Log { get; init; } = [];
}

/// <summary>
/// Request body for starting a full rehearsal (Feature 142).
/// <c>POST /api/blueprints/{id}/rehearsals</c>.
/// </summary>
public sealed record StartRehearsalRequest
{
    /// <summary>Rehearsal mode — only <see cref="RehearsalMode.Full"/> is valid over HTTP.</summary>
    [JsonPropertyName("mode")]
    public RehearsalMode Mode { get; init; } = RehearsalMode.Full;
}

/// <summary>
/// Request body for switching the acting participant role (Feature 142).
/// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/role</c>.
/// </summary>
public sealed record SwitchRehearsalRoleRequest
{
    /// <summary>The role to act as.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Request body for submitting the current action as the acting role (Feature 142).
/// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/steps</c>.
/// </summary>
public sealed record SubmitRehearsalStepRequest
{
    /// <summary>The action being submitted (must be the current step).</summary>
    [JsonPropertyName("actionId")]
    public required int ActionId { get; init; }

    /// <summary>The action payload as a raw JSON object string.</summary>
    [JsonPropertyName("payload")]
    public required string PayloadJson { get; init; }
}

/// <summary>
/// Optional override for the rehearsal soft gate when publishing (Feature 142). Present only
/// to bypass the "version not rehearsed" 409; requires register publish-governance authority
/// and records a PublishOverride audit row.
/// </summary>
public sealed record PublishOverride
{
    /// <summary>Must be true to override the soft gate.</summary>
    [JsonPropertyName("confirm")]
    public required bool Confirm { get; init; }

    /// <summary>Optional human-readable reason recorded in the audit row.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Request body for the CHANGED publish (Go live) endpoint (Feature 142).
/// <c>POST /api/blueprints/{id}/publish</c>.
/// </summary>
public sealed record PublishBlueprintRequest
{
    /// <summary>The target Go-live register.</summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>Present only to override the rehearsal soft gate.</summary>
    [JsonPropertyName("override")]
    public PublishOverride? Override { get; init; }
}

/// <summary>
/// Successful publish result (Feature 142). <c>200</c> from <c>POST /api/blueprints/{id}/publish</c>.
/// </summary>
public sealed record PublishBlueprintResult
{
    /// <summary>The published blueprint id.</summary>
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>The immutable published version number.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>The register the version was published to.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>When the version was published (UTC).</summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>True when the rehearsal soft gate was overridden.</summary>
    [JsonPropertyName("overridden")]
    public bool Overridden { get; init; }
}

/// <summary>
/// The <c>409 REHEARSAL_REQUIRED</c> soft-gate body (Feature 142). Returned when the
/// publishing version's executable-definition hash has no matching RehearsalPass; resend
/// the publish with an <see cref="PublishOverride"/> to proceed.
/// </summary>
public sealed record RehearsalRequiredError
{
    /// <summary>Stable machine code; always "REHEARSAL_REQUIRED".</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = "REHEARSAL_REQUIRED";

    /// <summary>The executable-definition hash that was not rehearsed.</summary>
    [JsonPropertyName("execDefHash")]
    public string ExecDefHash { get; init; } = string.Empty;

    /// <summary>Human-readable explanation.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Discriminated outcome of a publish attempt (Feature 142). Either the publish succeeded
/// (<see cref="Result"/> set) or it was blocked by the rehearsal soft gate
/// (<see cref="RehearsalRequired"/> set, HTTP 409).
/// </summary>
public sealed record PublishBlueprintOutcome
{
    /// <summary>The successful publish result, or null when blocked by the soft gate.</summary>
    public PublishBlueprintResult? Result { get; init; }

    /// <summary>The soft-gate 409 body, or null when the publish succeeded.</summary>
    public RehearsalRequiredError? RehearsalRequired { get; init; }

    /// <summary>True when the publish was blocked by the rehearsal soft gate (HTTP 409).</summary>
    public bool IsRehearsalRequired => RehearsalRequired is not null;
}

/// <summary>
/// Request body for deriving a new draft from a published version — the amend / clone-to-draft
/// flow (Feature 142). <c>POST /api/blueprints/from-published</c>.
/// </summary>
public sealed record CloneFromPublishedRequest
{
    /// <summary>The source register the published version lives on.</summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>The published blueprint id.</summary>
    [JsonPropertyName("blueprintId")]
    public required string BlueprintId { get; init; }

    /// <summary>The published version to clone.</summary>
    [JsonPropertyName("version")]
    public required int Version { get; init; }
}

/// <summary>
/// Result of cloning a published version to a fresh draft (Feature 142).
/// <c>201</c> from <c>POST /api/blueprints/from-published</c>. The new draft opens with
/// Go live re-locked pending a fresh rehearsal.
/// </summary>
public sealed record CloneFromPublishedResult
{
    /// <summary>The id of the newly-created draft.</summary>
    [JsonPropertyName("draftBlueprintId")]
    public string DraftBlueprintId { get; init; } = string.Empty;

    /// <summary>The published version the draft was derived from (lineage).</summary>
    [JsonPropertyName("sourceVersion")]
    public int SourceVersion { get; init; }

    /// <summary>The source register (lineage).</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;
}
