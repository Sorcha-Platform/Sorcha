// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Models;

/// <summary>
/// Status of a single dry-run walk-through step (Feature 142 / D3).
/// </summary>
public enum DryRunStepStatus
{
    /// <summary>The step has not yet been reached.</summary>
    Pending,

    /// <summary>The step is the one currently awaiting a submitted payload.</summary>
    Current,

    /// <summary>The step has been processed and the walk advanced.</summary>
    Done,

    /// <summary>The step's schema validation failed; the walk did not advance.</summary>
    Failed
}

/// <summary>
/// Per-step outcome of a quick dry-run (Feature 142 / D3 / T024). Captures, for one action in the
/// walk-through, the acting role, the payload submitted, and the engine's validation / routing /
/// disclosure outcomes. Conceptually aligns with the data-model <c>RehearsalStep</c> shape so the
/// designer UI (T025/T031) can render it directly.
/// </summary>
/// <remarks>
/// A dry-run covers schema validation, calculations, routing and disclosure only. When the action
/// declares credential prerequisites or issuance, those are <b>not executed</b>: instead
/// <see cref="CredentialNote"/> is set to flag that they are "checked in full rehearsal"
/// (Clarification Q3 / FR-018), so the dry-run is never mistaken for full fidelity.
/// </remarks>
public sealed class DryRunStep
{
    /// <summary>The action id this step corresponds to.</summary>
    public required int ActionId { get; init; }

    /// <summary>Human-readable action title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The participant id (role) acting on this step.</summary>
    public string ActingRole { get; init; } = string.Empty;

    /// <summary>The step's status within the walk-through.</summary>
    public DryRunStepStatus Status { get; set; } = DryRunStepStatus.Pending;

    /// <summary>The payload the acting role submitted for this step (null until submitted).</summary>
    public IReadOnlyDictionary<string, object>? SubmittedPayload { get; set; }

    /// <summary>Schema-validation result for the submitted payload.</summary>
    public ValidationResult? Validation { get; set; }

    /// <summary>Routing outcome — which action(s)/participant the workflow goes to next.</summary>
    public RoutingResult? RoutingOutcome { get; set; }

    /// <summary>Who-sees-what disclosure outcome for this step.</summary>
    public IReadOnlyList<DisclosureResult>? DisclosureOutcome { get; set; }

    /// <summary>
    /// Set when the action declares credential prerequisites and/or issuance. The dry-run does not
    /// execute these (no proof verification, no minting); the note records that they are checked in
    /// the full rehearsal instead. Null when the action involves no credentials.
    /// </summary>
    public string? CredentialNote { get; set; }

    /// <summary>True when this step involves credential prerequisites or issuance that the dry-run skipped.</summary>
    public bool HasDeferredCredentialChecks => CredentialNote is not null;
}
