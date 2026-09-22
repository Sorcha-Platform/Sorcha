// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// The handler behind <c>POST /api/execution/route</c> — the designer-time "what would this action
/// route to?" helper the MCP <c>sorcha_blueprint_simulate</c> tool calls (issue #1681).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is a named method rather than an inline lambda.</b> This endpoint answered
/// <c>nextActionId</c>/<c>nextParticipantId</c>/<c>isWorkflowComplete</c>/<c>matchedCondition</c>
/// only — the pre-Feature-184 shape — and never grew the <see cref="RoutingResult.NextActions"/>
/// list or <see cref="RoutingResult.MatchedRouteId"/> that <see cref="IRoutingEngine"/> has computed
/// correctly (Route-based routing takes precedence over the legacy <c>Condition</c> model) for a
/// long time. The MCP tool's client-side DTO was typed to a shape (<c>nextActions</c>, <c>matchedRoute</c>,
/// <c>routeDescription</c>) that this endpoint never wrote, so every simulation — regardless of
/// whether the action had zero, one, or several routes — silently deserialized to an empty
/// <c>NextActions</c> list and reported "No routing configured for this action". The routing engine
/// itself was never the defect; the wire contract between this endpoint and its caller was. A lambda
/// inside <c>Program.cs</c> is not reachable from a test, which is exactly how that drifted unnoticed
/// (the same trap CLAUDE.md pattern 25 / #1613 describes).
/// </para>
/// </remarks>
public static class ExecutionRoutingEndpoint
{
    /// <summary>
    /// Determines the next action(s) for a submitted payload against an action's routing rules
    /// (<see cref="Sorcha.Blueprint.Models.Action.Routes"/>, falling back to the legacy
    /// <see cref="Sorcha.Blueprint.Models.Action.Participants"/> conditions when no routes are
    /// declared) without executing the workflow.
    /// </summary>
    /// <param name="request">The blueprint, action and payload to route.</param>
    /// <param name="blueprintStore">Draft blueprint store.</param>
    /// <param name="executionEngine">The engine that evaluates routing.</param>
    /// <param name="logger">Logger for unexpected failures.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 with the full routing outcome, or 400 naming what could not be resolved.</returns>
    public static async Task<IResult> HandleAsync(
        RouteRequest request,
        IBlueprintStore blueprintStore,
        IExecutionEngine executionEngine,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
            if (blueprint is null)
            {
                return Results.BadRequest(new { error = "Blueprint not found" });
            }

            if (!int.TryParse(request.ActionId, out var actionIdInt))
            {
                return Results.BadRequest(new { error = "Invalid action ID format" });
            }

            var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
            if (action is null)
            {
                return Results.BadRequest(new { error = "Action not found in blueprint" });
            }

            var result = await executionEngine.DetermineRoutingAsync(blueprint, action, request.Data, ct);

            // Surface the route's own description (if any) — the MCP simulator reports this
            // verbatim so a designer sees WHY a branch was taken, not just its id.
            string? matchedRouteDescription = null;
            if (!string.IsNullOrEmpty(result.MatchedRouteId))
            {
                matchedRouteDescription = action.Routes?
                    .FirstOrDefault(r => string.Equals(r.Id, result.MatchedRouteId, StringComparison.Ordinal))
                    ?.Description;
            }

            return Results.Ok(new
            {
                nextActionId = result.NextActionId,
                nextParticipantId = result.NextParticipantId,
                isWorkflowComplete = result.IsWorkflowComplete,
                rejectedToParticipantId = result.RejectedToParticipantId,
                matchedCondition = result.MatchedCondition,
                matchedRouteId = result.MatchedRouteId,
                matchedRouteDescription,
                isParallel = result.IsParallel,
                // The field the MCP tool's RouteResponse DTO actually reads. Empty (not omitted)
                // both when this action truly has no routing configured AND when the matched route
                // is a terminal one with no next actions — isWorkflowComplete / matchedRouteId
                // distinguish those two cases for the caller.
                nextActions = result.NextActions.Select(a => BuildNextActionInfo(blueprint, a))
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Request failed");
            return Results.Problem("An error occurred processing the request.", statusCode: 400);
        }
    }

    private static object BuildNextActionInfo(Sorcha.Blueprint.Models.Blueprint blueprint, RoutedAction routed)
    {
        Sorcha.Blueprint.Models.Action? targetAction = null;
        if (int.TryParse(routed.ActionId, out var targetId))
        {
            targetAction = blueprint.Actions.FirstOrDefault(a => a.Id == targetId);
        }

        // A cheap, structural notion of "terminal": the target action itself declares no further
        // routing at all. This does not evaluate hypothetical future payloads — it only reports
        // whether ANY routing rule exists for that action.
        var isTerminal = targetAction is not null
            && targetAction.Routes?.Any() != true
            && targetAction.Participants?.Any() != true;

        return new
        {
            actionId = routed.ActionId,
            participantId = routed.ParticipantId,
            branchId = routed.BranchId,
            matchedRouteId = routed.MatchedRouteId,
            title = targetAction?.Title,
            isTerminal
        };
    }
}
