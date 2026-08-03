// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;
using BlueprintRoute = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 186 — turns an <see cref="Instance"/> plus the blueprint it runs into the citizen-facing
/// projection behind <c>/api/me/applications</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pure and static: every input is passed in, so the whole outcome-derivation table is testable
/// without a host, a store, or a mock. That matters because the derivation is the part that is easy
/// to get quietly wrong.
/// </para>
/// <para>
/// <b>Why an outcome exists separately from state.</b> Under Feature 184 a refusal is expressed as
/// taking a route that declares an <c>x-decision-notice</c> — not as a distinct instance state. When
/// such a route ends the branch, the fold sees an empty next-action set and assigns
/// <see cref="InstanceState.Completed"/>, so a refused application and an approved one are
/// indistinguishable by state alone. The projected <see cref="Instance.DecisionRouteId"/> is what
/// lets this recover the difference.
/// </para>
/// <para>
/// Everything here degrades rather than throws. A blueprint that is not replicated on this node, a
/// route id that no longer exists in it, a notice with no matching reason — each yields a row with
/// less on it, never an error and never invented wording (FR-013).
/// </para>
/// </remarks>
public static class MyApplicationProjector
{
    /// <summary>Metadata key the imperative creation path stamps the blueprint title under.</summary>
    private const string BlueprintTitleKey = "BlueprintTitle";

    /// <summary>Metadata key the first sealed action stamps the human-readable reference under.</summary>
    private const string InstanceReferenceKey = "instanceReference";

    /// <summary>Outcome reported when the application ended on an adversely-flagged decision route.</summary>
    private const string NotApprovedOutcome = "NotApproved";

    /// <summary>
    /// Severities that mark a decision as adverse. Anything else — including a notice carrying good
    /// news — leaves the outcome as the lifecycle state's own name.
    /// </summary>
    private static readonly string[] AdverseSeverities = ["Warning", "Error"];

    /// <summary>
    /// Projects one list row.
    /// </summary>
    /// <param name="instance">The application.</param>
    /// <param name="blueprint">The blueprint it runs, or null when not replicated on this node.</param>
    /// <param name="callerWallets">Wallet addresses the calling citizen controls.</param>
    /// <returns>The citizen-facing row.</returns>
    public static MyApplicationSummary Project(
        Instance instance,
        BlueprintModel? blueprint,
        IReadOnlyCollection<string> callerWallets)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(callerWallets);

        var actions = OrderedActions(blueprint);
        var currentActionId = instance.CurrentActionIds.Count > 0
            ? instance.CurrentActionIds.Min()
            : (int?)null;
        var currentAction = currentActionId is null
            ? null
            : actions.FirstOrDefault(a => a.Id == currentActionId.Value);

        var notice = ResolveTakenNotice(instance, blueprint);
        var state = instance.State.ToString();

        return new MyApplicationSummary
        {
            InstanceId = instance.Id,
            BlueprintId = instance.BlueprintId,
            BlueprintTitle = ResolveTitle(instance, blueprint),
            InstanceReference = NullIfBlank(Lookup(instance, InstanceReferenceKey)),
            State = state,
            Outcome = DeriveOutcome(state, notice),
            DecisionTitle = notice is null ? null : NullIfBlank(notice.Title),
            DecisionReason = notice is null
                ? null
                : NullIfBlank(notice.ResolveMessage(instance.DecisionReasonCode)),
            DecisionSeverity = notice is null ? null : Severity(notice),
            CurrentActionId = currentActionId,
            CurrentActionTitle = currentAction is null ? null : NullIfBlank(currentAction.Title),
            StepNumber = StepNumber(actions, currentActionId),
            TotalSteps = actions.Count > 0 ? actions.Count : null,
            NeedsYou = NeedsYou(instance, currentAction, callerWallets),
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
        };
    }

    /// <summary>
    /// Projects the detail view: the list row plus the service's steps marked against this
    /// application.
    /// </summary>
    /// <param name="instance">The application.</param>
    /// <param name="blueprint">The blueprint it runs, or null when not replicated on this node.</param>
    /// <param name="callerWallets">Wallet addresses the calling citizen controls.</param>
    /// <returns>The citizen-facing detail.</returns>
    public static MyApplicationDetail ProjectDetail(
        Instance instance,
        BlueprintModel? blueprint,
        IReadOnlyCollection<string> callerWallets)
    {
        var summary = Project(instance, blueprint, callerWallets);
        var actions = OrderedActions(blueprint);

        var steps = actions.Select(action => new MyApplicationStep(
            action.Id,
            string.IsNullOrWhiteSpace(action.Title) ? $"Step {action.Id}" : action.Title,
            StepStatus(action.Id, instance, summary.StepNumber, actions))).ToList();

        return new MyApplicationDetail { Summary = summary, Steps = steps };
    }

    /// <summary>
    /// The outcome table (specs/186 data-model.md §5). Ordered; first match wins.
    /// </summary>
    private static string DeriveOutcome(string state, Sorcha.Blueprint.Models.DecisionNotice? notice)
    {
        if (notice is null)
            return state;   // no decision, unresolvable blueprint/route, or a route declaring no notice

        return AdverseSeverities.Contains(Severity(notice), StringComparer.OrdinalIgnoreCase)
            ? NotApprovedOutcome
            : state;
    }

    /// <summary>
    /// Finds the decision notice on the route the sender actually took. Returns null whenever any
    /// link in that chain is missing — no recorded route, no local blueprint, a route id the
    /// blueprint no longer declares, or a route carrying no notice.
    /// </summary>
    private static Sorcha.Blueprint.Models.DecisionNotice? ResolveTakenNotice(
        Instance instance, BlueprintModel? blueprint)
    {
        if (string.IsNullOrWhiteSpace(instance.DecisionRouteId) || blueprint?.Actions is null)
            return null;

        return blueprint.Actions
            .SelectMany(a => a.Routes ?? Enumerable.Empty<BlueprintRoute>())
            .FirstOrDefault(r => string.Equals(r.Id, instance.DecisionRouteId, StringComparison.Ordinal))
            ?.DecisionNotice;
    }

    /// <summary>Notice severity, defaulting to Warning exactly as the inbox dispatcher does.</summary>
    private static string Severity(Sorcha.Blueprint.Models.DecisionNotice notice) =>
        string.IsNullOrWhiteSpace(notice.Severity) ? "Warning" : notice.Severity!;

    /// <summary>
    /// True only when the application is live AND the current action's sender participant is bound
    /// to a wallet the caller controls. Fails closed on every uncertainty, so the page cannot offer
    /// an action that turns out not to be takeable (#1268).
    /// </summary>
    private static bool NeedsYou(
        Instance instance, BlueprintAction? currentAction, IReadOnlyCollection<string> callerWallets)
    {
        if (instance.State != InstanceState.Active || currentAction is null)
            return false;

        if (string.IsNullOrWhiteSpace(currentAction.Sender))
            return false;

        return instance.ParticipantWallets.TryGetValue(currentAction.Sender, out var wallet)
               && !string.IsNullOrWhiteSpace(wallet)
               && callerWallets.Contains(wallet, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveTitle(Instance instance, BlueprintModel? blueprint)
    {
        var cached = Lookup(instance, BlueprintTitleKey);
        if (!string.IsNullOrWhiteSpace(cached))
            return cached!;

        // Instances created by the ledger projector never populate the cached title — only the
        // imperative creation path does — so this arm is the normal case, not a fallback.
        return string.IsNullOrWhiteSpace(blueprint?.Title) ? instance.BlueprintId : blueprint!.Title;
    }

    private static IReadOnlyList<BlueprintAction> OrderedActions(BlueprintModel? blueprint) =>
        blueprint?.Actions?.OrderBy(a => a.Id).ToList() ?? [];

    private static int? StepNumber(IReadOnlyList<BlueprintAction> actions, int? currentActionId)
    {
        if (currentActionId is null)
            return null;

        var index = actions.ToList().FindIndex(a => a.Id == currentActionId.Value);
        return index < 0 ? null : index + 1;
    }

    private static string StepStatus(
        int actionId, Instance instance, int? stepNumber, IReadOnlyList<BlueprintAction> actions)
    {
        if (instance.CurrentActionIds.Contains(actionId))
            return "Current";

        if (stepNumber is null)
            return "Completed";   // terminal: nothing is upcoming

        var index = actions.ToList().FindIndex(a => a.Id == actionId);
        return index >= 0 && index + 1 < stepNumber.Value ? "Completed" : "Upcoming";
    }

    private static string? Lookup(Instance instance, string key) =>
        instance.Metadata.TryGetValue(key, out var value) ? value : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
