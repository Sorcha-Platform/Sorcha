// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Routing engine that determines the next action and participant in a workflow.
/// </summary>
/// <remarks>
/// Uses conditional routing based on JSON Logic expressions to determine
/// which participant should perform the next action.
/// 
/// Thread-safe and can be used concurrently.
/// </remarks>
public class RoutingEngine : IRoutingEngine
{
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.Engine.Routing");
    private readonly IJsonLogicEvaluator _evaluator;

    public RoutingEngine(IJsonLogicEvaluator evaluator)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    }

    /// <summary>
    /// Determine the next action and participant based on routing conditions.
    /// Routes (if defined) take precedence over legacy Condition-based routing.
    /// </summary>
    public Task<RoutingResult> DetermineNextAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action currentAction,
        Dictionary<string, object> data,
        CancellationToken ct = default)
        => DetermineNextWithMappingAsync(blueprint, currentAction, data, outputSource: null, ct);

    /// <inheritdoc />
    public async Task<RoutingResult> DetermineNextWithMappingAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action currentAction,
        Dictionary<string, object> data,
        JsonObject? outputSource,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(currentAction);
        ArgumentNullException.ThrowIfNull(data);

        // Build action index once for O(1) lookups during routing
        var actionIndex = blueprint.BuildActionIndex();

        // Route-based routing takes precedence
        var routes = currentAction.Routes?.ToList();
        if (routes != null && routes.Count > 0)
        {
            return EvaluateRoutes(actionIndex, routes, data, outputSource);
        }

        // Legacy: Condition-based routing via Participants
        return await EvaluateLegacyConditionsAsync(blueprint, actionIndex, currentAction, data, ct);
    }

    /// <summary>
    /// Evaluates Route-based routing: iterates routes in order,
    /// first matching condition wins, default route used as fallback.
    /// </summary>
    private RoutingResult EvaluateRoutes(
        Dictionary<int, Sorcha.Blueprint.Models.Action> actionIndex,
        List<Route> routes,
        Dictionary<string, object> data,
        JsonObject? outputSource)
    {
        Route? defaultRoute = null;

        foreach (var route in routes)
        {
            // Save default route for fallback
            if (route.Condition == null && route.IsDefault)
            {
                defaultRoute = route;
                continue;
            }

            // Skip routes with no condition and not default (invalid config)
            if (route.Condition == null)
            {
                continue;
            }

            // Evaluate JSON Logic condition
            var result = _evaluator.Evaluate(route.Condition, data);
            if (IsTruthy(result))
            {
                var routingResult = BuildRoutingResult(actionIndex, route, data);
                routingResult.PendingPayloads = EvaluateOutputMapping(route, outputSource);
                return routingResult;
            }
        }

        // Use default route if no conditions matched
        if (defaultRoute != null)
        {
            var routingResult = BuildRoutingResult(actionIndex, defaultRoute, data);
            routingResult.PendingPayloads = EvaluateOutputMapping(defaultRoute, outputSource);
            return routingResult;
        }

        // No routes matched
        return RoutingResult.Complete();
    }

    /// <summary>
    /// Evaluates <see cref="Route.OutputMapping"/> against the provided output source
    /// document and returns per-next-action prepopulated payloads. Returns null when
    /// the route declares no mapping, the source is null, or the route has no next actions.
    /// Absent source paths are silently skipped per Feature 104 wave 14a design.
    /// </summary>
    private static IReadOnlyDictionary<int, JsonObject>? EvaluateOutputMapping(
        Route route,
        JsonObject? outputSource)
    {
        var mapping = route.OutputMapping;
        if (mapping == null || mapping.Count == 0 || outputSource == null)
        {
            return null;
        }

        var nextActionIds = route.NextActionIds?.ToList() ?? [];
        if (nextActionIds.Count == 0)
        {
            return null;
        }

        using var activity = ActivitySource.StartActivity("blueprint.routing.output_mapping.evaluate");
        activity?.SetTag("route.id", route.Id);
        activity?.SetTag("mapping.entry_count", mapping.Count);
        activity?.SetTag("next_action_count", nextActionIds.Count);

        // Build the seed once; deep-clone per next action to avoid shared mutable state
        var template = new JsonObject();
        var appliedEntries = 0;

        foreach (var (sourcePointer, targetPointer) in mapping)
        {
            if (!JsonPointerHelper.TryResolve(outputSource, sourcePointer, out var sourceValue))
            {
                // Absent source — silently skip per spec
                continue;
            }

            if (!JsonPointerHelper.TrySet(template, targetPointer, sourceValue))
            {
                // Invalid target pointer — skip rather than abort routing
                continue;
            }

            appliedEntries++;
        }

        activity?.SetTag("mapping.applied_entries", appliedEntries);

        if (appliedEntries == 0)
        {
            return null;
        }

        // Deep-clone the template for each next action so they don't share mutable state
        var result = new Dictionary<int, JsonObject>(capacity: nextActionIds.Count);
        foreach (var nextId in nextActionIds)
        {
            var cloned = JsonNode.Parse(template.ToJsonString()) as JsonObject ?? new JsonObject();
            result[nextId] = cloned;
        }

        return result;
    }

    /// <summary>
    /// Builds a RoutingResult from a matched Route.
    /// Single NextActionId → Next(), multiple → Parallel().
    /// </summary>
    private static RoutingResult BuildRoutingResult(
        Dictionary<int, Sorcha.Blueprint.Models.Action> actionIndex,
        Route route,
        Dictionary<string, object> data)
    {
        var nextActionIds = route.NextActionIds?.ToList() ?? [];
        if (nextActionIds.Count == 0)
        {
            // Terminal route. It produces no RoutedAction, so the taken route's identity would be
            // lost were it not reported at the top level (Feature 184) — and a decision workflow's
            // reject route is precisely a terminal route.
            var terminal = RoutingResult.Complete();
            terminal.MatchedRouteId = route.Id;
            terminal.MatchedCondition = route.Condition?.ToJsonString();
            return terminal;
        }

        var conditionStr = route.Condition?.ToJsonString();

        var routedActions = nextActionIds.Select((actionId, index) =>
        {
            actionIndex.TryGetValue(actionId, out var targetAction);
            return new RoutedAction
            {
                ActionId = actionId.ToString(),
                ParticipantId = targetAction?.Sender,
                BranchId = nextActionIds.Count > 1 ? $"{route.Id ?? "route"}-branch-{index}" : null,
                MatchedRouteId = route.Id
            };
        }).ToList();

        if (routedActions.Count == 1)
        {
            return new RoutingResult
            {
                NextActionId = routedActions[0].ActionId,
                NextParticipantId = routedActions[0].ParticipantId ?? "",
                MatchedCondition = conditionStr,
                MatchedRouteId = route.Id,
                NextActions = routedActions
            };
        }

        var parallel = RoutingResult.Parallel(routedActions, conditionStr);
        parallel.MatchedRouteId = route.Id;
        return parallel;
    }

    /// <summary>
    /// Legacy Condition-based routing via Action.Participants.
    /// </summary>
    private async Task<RoutingResult> EvaluateLegacyConditionsAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Dictionary<int, Sorcha.Blueprint.Models.Action> actionIndex,
        Sorcha.Blueprint.Models.Action currentAction,
        Dictionary<string, object> data,
        CancellationToken ct)
    {
        var conditions = currentAction.Participants?.ToList() ?? new List<Condition>();

        if (!conditions.Any())
        {
            return RoutingResult.Complete();
        }

        var nextParticipantId = await _evaluator.EvaluateConditionsAsync(data, conditions, ct);

        if (nextParticipantId == null)
        {
            return RoutingResult.Complete();
        }

        var nextAction = FindNextActionForParticipant(blueprint, currentAction, nextParticipantId);

        if (nextAction == null)
        {
            return RoutingResult.Complete();
        }

        var matchedCondition = conditions
            .FirstOrDefault(c => c.Principal == nextParticipantId)?.Criteria
            .FirstOrDefault();

        return RoutingResult.Next(
            nextAction.Id.ToString(),
            nextParticipantId,
            matchedCondition
        );
    }

    /// <summary>
    /// Determines if a JSON Logic evaluation result is truthy.
    /// </summary>
    private static bool IsTruthy(object? result) => result switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true
    };

    /// <summary>
    /// Finds the next action for a participant in the blueprint.
    /// </summary>
    /// <remarks>
    /// The next action is typically the action with an ID greater than
    /// the current action, but this is a simplified implementation.
    /// In a real system, you might have explicit routing references.
    /// </remarks>
    private static Sorcha.Blueprint.Models.Action? FindNextActionForParticipant(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action currentAction,
        string participantId)
    {
        // Get all actions after the current one
        var currentIndex = blueprint.Actions.FindIndex(a => a.Id == currentAction.Id);
        
        if (currentIndex < 0)
        {
            // Current action not found in blueprint
            return null;
        }

        // Look for the next action that this participant can perform
        // This is a simple linear search - production systems might use
        // explicit action references or more complex routing logic
        for (int i = currentIndex + 1; i < blueprint.Actions.Count; i++)
        {
            var action = blueprint.Actions[i];
            
            // Check if this action can be performed by the participant
            // by looking at the action's sender or participants
            if (action.Sender == participantId)
            {
                return action;
            }

            // Also check if the participant is in the action's participants list
            if (action.Participants?.Any(p => p.Principal == participantId) == true)
            {
                return action;
            }
        }

        // No next action found for this participant
        return null;
    }
}
