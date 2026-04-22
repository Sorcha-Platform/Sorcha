// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Declarative non-reactive behaviour for a Sorcha agent.
/// Loaded from a JSON file referenced by an actor config's <c>personaFile</c> field.
/// </summary>
public record PersonaDefinition
{
    public required string Name { get; init; }
    public required PersonaTarget Target { get; init; }
    public required PersonaTrigger Trigger { get; init; }
    public required JsonNode PayloadTemplate { get; init; }
}

/// <summary>
/// Target of a persona submission: blueprint, instance, and starting action.
/// </summary>
public record PersonaTarget
{
    public required string BlueprintId { get; init; }
    public required string InstanceId { get; init; }
    public string? ActionName { get; init; }
    public int? ActionIndex { get; init; }
}

/// <summary>
/// Base class for persona triggers. v1 supports <see cref="OnceTrigger"/> and <see cref="IntervalTrigger"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(OnceTrigger), "once")]
[JsonDerivedType(typeof(IntervalTrigger), "interval")]
public abstract record PersonaTrigger;

/// <summary>
/// Fires exactly one submission after the agent starts, then exits.
/// </summary>
public sealed record OnceTrigger : PersonaTrigger
{
    public int DelaySeconds { get; init; } = 0;
}

/// <summary>
/// Fires repeated submissions at a declared cadence, bounded by optional stop conditions.
/// </summary>
public sealed record IntervalTrigger : PersonaTrigger
{
    public int? EverySeconds { get; init; }
    public int? EveryMinutes { get; init; }
    public int StartDelaySeconds { get; init; } = 0;
    public int? MaxIterations { get; init; }
    public DateTimeOffset? Until { get; init; }

    /// <summary>
    /// Normalised interval in seconds. Exactly one of <see cref="EverySeconds"/> or
    /// <see cref="EveryMinutes"/> must be set; schema validation enforces this.
    /// </summary>
    public int IntervalSeconds => EverySeconds ?? (EveryMinutes!.Value * 60);
}

/// <summary>
/// Runtime context passed to the payload token resolver on each persona fire.
/// </summary>
public record PersonaFireContext
{
    public required int Iteration { get; init; }
    public required DateTimeOffset Now { get; init; }
    public required IRandomSource RandomSource { get; init; }
}

/// <summary>
/// Outcome of a single persona submission.
/// </summary>
public enum PersonaSubmissionOutcome
{
    Submitted,
    TransientFailure,
    HardFailure
}

/// <summary>
/// Result of a single persona submission.
/// </summary>
public record PersonaSubmissionResult(
    PersonaSubmissionOutcome Outcome,
    long DurationMs,
    string? Error = null);
