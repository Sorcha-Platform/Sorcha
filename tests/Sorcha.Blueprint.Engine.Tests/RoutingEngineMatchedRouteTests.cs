// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Feature 184 — the routing engine must report <b>which route it took</b> on every matched-route
/// path, including a terminal route (empty <c>nextActionIds</c>). The reject route of a decision
/// workflow is exactly that case: it has no next actions, so the per-next-action
/// <see cref="Models.RoutedAction.MatchedRouteId"/> cannot carry the identity. Downstream, the taken
/// route's id rides the signed routing decision so the recipient's node can find the route's
/// <c>x-decision-notice</c> in the replicated blueprint without re-evaluating any condition.
/// </summary>
public class RoutingEngineMatchedRouteTests
{
    private static RoutingEngine CreateEngine() => new(new JsonLogicEvaluator());

    private static Sorcha.Blueprint.Models.Blueprint BlueprintWith(Sorcha.Blueprint.Models.Action action)
        => new()
        {
            Title = "Decision workflow",
            Participants =
            [
                new Participant { Id = "citizen" },
                new Participant { Id = "analyst" },
            ],
            Actions = [action, new Sorcha.Blueprint.Models.Action { Id = 3, Title = "Claim", Sender = "citizen" }],
        };

    private static Sorcha.Blueprint.Models.Action DecisionAction(params Route[] routes)
        => new()
        {
            Id = 2,
            Title = "Verify application",
            Sender = "analyst",
            Routes = routes.ToList(),
        };

    [Fact]
    public async Task DetermineNext_TerminalRoute_ReportsMatchedRouteId()
    {
        // The reject route: condition matches, and it routes nowhere (workflow ends).
        var action = DecisionAction(
            new Route
            {
                Id = "approved-to-claim",
                NextActionIds = [3],
                Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }"""),
            },
            new Route
            {
                Id = "rejected-terminal",
                NextActionIds = [],
                Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "rejected" ] }"""),
            });

        var result = await CreateEngine().DetermineNextAsync(
            BlueprintWith(action), action, new Dictionary<string, object> { ["decision"] = "rejected" });

        Assert.Empty(result.NextActions);
        Assert.True(result.IsWorkflowComplete);
        Assert.Equal("rejected-terminal", result.MatchedRouteId);
    }

    [Fact]
    public async Task DetermineNext_ConditionalRoute_ReportsMatchedRouteId()
    {
        var action = DecisionAction(
            new Route
            {
                Id = "approved-to-claim",
                NextActionIds = [3],
                Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }"""),
            });

        var result = await CreateEngine().DetermineNextAsync(
            BlueprintWith(action), action, new Dictionary<string, object> { ["decision"] = "approved" });

        Assert.Equal("approved-to-claim", result.MatchedRouteId);
        Assert.Equal("3", result.NextActions.Single().ActionId);
    }

    [Fact]
    public async Task DetermineNext_DefaultRoute_ReportsMatchedRouteId()
    {
        var action = DecisionAction(
            new Route
            {
                Id = "approved-to-claim",
                NextActionIds = [3],
                Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }"""),
            },
            new Route { Id = "fallback-terminal", NextActionIds = [], IsDefault = true });

        var result = await CreateEngine().DetermineNextAsync(
            BlueprintWith(action), action, new Dictionary<string, object> { ["decision"] = "something-else" });

        Assert.Equal("fallback-terminal", result.MatchedRouteId);
    }

    [Fact]
    public async Task DetermineNext_NoRouteMatches_ReportsNoMatchedRouteId()
    {
        var action = DecisionAction(
            new Route
            {
                Id = "approved-to-claim",
                NextActionIds = [3],
                Condition = JsonNode.Parse("""{ "==": [ { "var": "decision" }, "approved" ] }"""),
            });

        var result = await CreateEngine().DetermineNextAsync(
            BlueprintWith(action), action, new Dictionary<string, object> { ["decision"] = "rejected" });

        Assert.Null(result.MatchedRouteId);
    }
}
