// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Components.Designer;

/// <summary>
/// Backend-agnostic presentation status for a rehearsal step (Feature 142 / T025). The shared
/// <c>RehearsalStepper</c> renders one of these regardless of whether the step came from the
/// in-WASM dry-run (<c>DryRunStep</c>) or the server-side full rehearsal (<c>RehearsalStep</c>).
/// </summary>
public enum RehearsalStepViewStatus
{
    /// <summary>Not yet reached.</summary>
    Pending,

    /// <summary>The step awaiting submission right now.</summary>
    Current,

    /// <summary>Submitted and applied; the walk advanced.</summary>
    Done,

    /// <summary>The step's validation failed; the walk did not advance.</summary>
    Failed,
}

/// <summary>
/// Classification of a plain-language rehearsal log entry (Feature 142 / T025). Mirrors the wire
/// <c>RehearsalEventKind</c> but stays UI-local so the shared stepper has no service-client
/// dependency.
/// </summary>
public enum RehearsalLogKind
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
    Error,
}

/// <summary>
/// One step in the shared rehearsal stepper's view (Feature 142 / T025). A portable projection of
/// either an engine <c>DryRunStep</c> or a wire <c>RehearsalStep</c> so the stepper is decoupled
/// from whichever backend produced the walk-through.
/// </summary>
/// <param name="ActionId">The blueprint action this step exercises.</param>
/// <param name="Title">Human-readable action title (falls back to "Action {id}" when empty).</param>
/// <param name="ActingRole">The participant role acting on this step.</param>
/// <param name="Status">Walk-through status of this step.</param>
/// <param name="RoutingSummary">Plain-language routing outcome, or null when none.</param>
/// <param name="DisclosureSummary">Plain-language who-sees-what summary, or null when none.</param>
/// <param name="CredentialNote">The "checked in full rehearsal" note when credentials were deferred; null otherwise.</param>
public sealed record RehearsalStepView(
    int ActionId,
    string Title,
    string ActingRole,
    RehearsalStepViewStatus Status,
    string? RoutingSummary = null,
    string? DisclosureSummary = null,
    string? CredentialNote = null)
{
    /// <summary>True when this is the step currently awaiting submission.</summary>
    public bool IsCurrent => Status == RehearsalStepViewStatus.Current;

    /// <summary>True when this step involves credential prerequisites / issuance deferred to the full rehearsal.</summary>
    public bool HasDeferredCredentialChecks => CredentialNote is not null;
}

/// <summary>
/// One entry in the shared rehearsal stepper's plain-language activity log (Feature 142 / T025).
/// </summary>
/// <param name="Message">User-visible plain-language message.</param>
/// <param name="Kind">Classification used for the entry's icon / colour.</param>
public sealed record RehearsalLogView(string Message, RehearsalLogKind Kind);
