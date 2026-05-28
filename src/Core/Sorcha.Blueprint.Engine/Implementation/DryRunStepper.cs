// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Engine-local, dependency-free driver for the Feature 142 quick dry-run (D3 / T024 core logic).
/// Given a <see cref="Sorcha.Blueprint.Models.Blueprint"/> and an <see cref="IWalkState"/>, it processes one action at a
/// time — validate → calculate → route → disclose — entirely in memory, accumulating the
/// walk-through's state via the supplied <see cref="IWalkState"/> so routing and disclosure see
/// prior-action data across steps.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately reproduces the canonical engine pipeline (the same steps
/// <see cref="ActionProcessor"/> runs for a full execution) <b>except</b> credential prerequisites
/// and credential issuance: those are <i>not</i> executed. When an action declares
/// <see cref="Sorcha.Blueprint.Models.Action.CredentialRequirements"/> or
/// <see cref="Sorcha.Blueprint.Models.Action.CredentialIssuanceConfig"/>, the produced
/// <see cref="DryRunStep"/> is annotated <see cref="CheckedInFullRehearsalNote"/> rather than
/// exercising the credential pipeline (Clarification Q3 / FR-018).
/// </para>
/// <para>
/// The routing and disclosure outcomes this driver produces are exactly the engine's canonical
/// outcomes — it calls <see cref="IExecutionEngine.DetermineRoutingAsync"/> and
/// <see cref="IExecutionEngine.ApplyDisclosures"/> directly, so the dry-run cannot diverge from
/// real execution for the behaviours it covers.
/// </para>
/// <para>WASM-safe: no <c>HttpClient</c>, no platform APIs.</para>
/// </remarks>
public sealed class DryRunStepper
{
    /// <summary>The annotation set on steps whose credential checks the dry-run skipped.</summary>
    public const string CheckedInFullRehearsalNote = "checked in full rehearsal";

    private readonly IExecutionEngine _engine;
    private readonly IWalkState _walkState;

    /// <summary>Creates a stepper bound to an execution engine and an accumulated-state holder.</summary>
    /// <param name="engine">The portable execution engine.</param>
    /// <param name="walkState">The engine-local in-memory accumulated state for this walk-through.</param>
    public DryRunStepper(IExecutionEngine engine, IWalkState walkState)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _walkState = walkState ?? throw new ArgumentNullException(nameof(walkState));
    }

    /// <summary>
    /// Determines the action the walk-through should start on: the starting action if declared,
    /// otherwise the lowest-id action.
    /// </summary>
    /// <param name="blueprint">The blueprint being rehearsed.</param>
    /// <returns>The first action, or null when the blueprint has no actions.</returns>
    public static Sorcha.Blueprint.Models.Action? ResolveStartAction(Sorcha.Blueprint.Models.Blueprint blueprint)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        return blueprint.Actions.FirstOrDefault(a => a.IsStartingAction)
               ?? blueprint.Actions.OrderBy(a => a.Id).FirstOrDefault();
    }

    /// <summary>
    /// Processes the current action of the walk-through against a submitted payload, running
    /// validate → calculate → route → disclose and (on success) advancing the accumulated state.
    /// Credential prerequisites/issuance are NOT executed — they are flagged
    /// <see cref="CheckedInFullRehearsalNote"/> instead.
    /// </summary>
    /// <param name="blueprint">The blueprint being rehearsed.</param>
    /// <param name="action">The action to process (the current step).</param>
    /// <param name="submittedPayload">The payload the acting role submits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The per-step outcome, conceptually aligned with the data-model RehearsalStep.</returns>
    public async Task<DryRunStep> ProcessStepAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        IReadOnlyDictionary<string, object> submittedPayload,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(submittedPayload);

        _walkState.SetCurrentAction(action.Id);

        var step = new DryRunStep
        {
            ActionId = action.Id,
            Title = action.Title,
            ActingRole = action.Sender,
            Status = DryRunStepStatus.Current,
            SubmittedPayload = new Dictionary<string, object>(submittedPayload, StringComparer.Ordinal),
            CredentialNote = BuildCredentialNote(action),
        };

        // Build the merged input: prior accumulated state + this step's submission.
        var mergedInput = _walkState.BuildMergedInput(submittedPayload);

        // 1. Schema validation (canonical engine outcome).
        var validation = await _engine.ValidateAsync(mergedInput, action, ct);
        step.Validation = validation;
        if (!validation.IsValid)
        {
            step.Status = DryRunStepStatus.Failed;
            return step;
        }

        // 2. Calculations — fold calculated fields into the working data.
        var processed = await _engine.ApplyCalculationsAsync(mergedInput, action, ct);

        // 3. Routing (canonical engine outcome — identical to real execution).
        step.RoutingOutcome = await _engine.DetermineRoutingAsync(blueprint, action, processed, ct);

        // 4. Disclosure (canonical engine outcome — identical to real execution).
        step.DisclosureOutcome = _engine.ApplyDisclosures(processed, action);

        // Advance accumulated state with the action's processed data so later steps can route on it.
        _walkState.RecordCompletedAction(action.Id, processed);
        step.Status = DryRunStepStatus.Done;

        return step;
    }

    /// <summary>
    /// Resolves the next action to step onto from a completed step's routing outcome, or null when
    /// the workflow is complete / rejected / the next action cannot be found.
    /// </summary>
    /// <param name="blueprint">The blueprint being rehearsed.</param>
    /// <param name="routing">The routing outcome of the just-completed step.</param>
    /// <returns>The next action, or null to end the walk-through.</returns>
    public static Sorcha.Blueprint.Models.Action? ResolveNextAction(Sorcha.Blueprint.Models.Blueprint blueprint, RoutingResult routing)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(routing);

        if (routing.IsWorkflowComplete || routing.RejectedToParticipantId is not null)
        {
            return null;
        }

        // Single next action (or first branch of a parallel result) drives a linear walk-through.
        var nextId = routing.NextActions.FirstOrDefault()?.ActionId ?? routing.NextActionId;
        if (string.IsNullOrEmpty(nextId) || !int.TryParse(nextId, out var id))
        {
            return null;
        }

        return blueprint.Actions.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>
    /// Builds the "checked in full rehearsal" note for an action that has credential prerequisites
    /// and/or issuance, or null when the action involves no credentials.
    /// </summary>
    private static string? BuildCredentialNote(Sorcha.Blueprint.Models.Action action)
    {
        var requiresProof = action.CredentialRequirements?.Any() == true;
        var issues = action.CredentialIssuanceConfig is not null;

        if (!requiresProof && !issues)
        {
            return null;
        }

        if (requiresProof && issues)
        {
            return $"Credential prerequisite and issuance — {CheckedInFullRehearsalNote}.";
        }

        return requiresProof
            ? $"Credential prerequisite — {CheckedInFullRehearsalNote}."
            : $"Credential issuance — {CheckedInFullRehearsalNote}.";
    }
}
