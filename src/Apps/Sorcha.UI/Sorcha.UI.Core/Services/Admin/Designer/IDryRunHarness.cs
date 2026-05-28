// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Drives the Feature 142 quick dry-run (D3 / FR-018) for the designer's "Rehearse" stage:
/// steps a <see cref="BlueprintModel"/> through the portable engine in-browser (validate →
/// calculate → route → disclose) with no register and no backend round-trip, exposing a per-step
/// model the UI can render. Credential prerequisites/issuance are flagged "checked in full
/// rehearsal", never executed.
/// </summary>
public interface IDryRunHarness
{
    /// <summary>The per-step outcomes recorded so far, in walk order.</summary>
    IReadOnlyList<DryRunStep> Steps { get; }

    /// <summary>The action id awaiting submission, or null when the walk-through is complete.</summary>
    int? CurrentActionId { get; }

    /// <summary>The participant id (role) the author is currently acting as, or null when complete.</summary>
    string? CurrentActingRole { get; }

    /// <summary>True once a walk-through has been started.</summary>
    bool IsStarted { get; }

    /// <summary>True when the walk-through has been started and has reached its end.</summary>
    bool IsComplete { get; }

    /// <summary>
    /// Begins a fresh dry-run over <paramref name="blueprint"/>, clearing any prior state and
    /// positioning the walk on the starting action.
    /// </summary>
    /// <param name="blueprint">The blueprint to rehearse.</param>
    void Start(BlueprintModel blueprint);

    /// <summary>
    /// Processes the current step against <paramref name="submittedPayload"/> and, on success,
    /// advances to the routed next action. On validation failure the walk stays on the current
    /// action so the author can correct the payload.
    /// </summary>
    /// <param name="submittedPayload">The payload the acting role submits for the current step.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The per-step outcome (status, routing, disclosure, credential note).</returns>
    Task<DryRunStep> SubmitCurrentStepAsync(
        IReadOnlyDictionary<string, object> submittedPayload,
        CancellationToken ct = default);

    /// <summary>Restarts the walk-through over the same blueprint (or clears state if none loaded).</summary>
    void Reset();
}
