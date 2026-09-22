// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Storage;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Issue #1681 — <c>POST /api/execution/route</c> (the endpoint behind the MCP
/// <c>sorcha_blueprint_simulate</c> tool) never grew the <c>nextActions</c> /
/// <c>matchedRouteId</c> / <c>isWorkflowComplete</c> fields that
/// <see cref="Sorcha.Blueprint.Engine.Models.RoutingResult"/> has carried since Feature 184's
/// Route model was added. It kept answering with the pre-Routes shape
/// (<c>nextActionId</c>/<c>nextParticipantId</c>/<c>matchedCondition</c> only), so a caller typed
/// to read <c>nextActions</c> — the MCP tool's <c>RouteResponse</c> DTO — always deserialized an
/// empty list and reported "No routing configured for this action", regardless of what the routing
/// engine actually decided.
/// </summary>
/// <remarks>
/// <para>
/// The engine here is the <b>real</b> one (<see cref="RoutingEngine"/> + <see cref="JsonLogicEvaluator"/>),
/// not a mock — a mocked <c>IExecutionEngine</c> would only assert the endpoint echoes whatever it is
/// handed, which is exactly the shape the broken version already produced.
/// </para>
/// <para>
/// The fixture's routes are copied verbatim from the shape a real published blueprint uses —
/// <c>walkthroughs/CyberEssentialsUac/ce-uac-assessment-template.json</c>, action 0: two routes, one
/// conditional (<c>compliant-issue</c>) and one default (<c>noncompliant-record</c>) — exactly the
/// "two routes including a conditional one" shape issue #1681 reports live execution honouring
/// correctly while the simulator disagreed.
/// </para>
/// </remarks>
public class ExecutionRoutingEndpointTests
{
    private const string BlueprintId = "bp-uac-1";

    private static IExecutionEngine RealEngine()
    {
        var schemaValidator = new SchemaValidator();
        var jsonLogic = new JsonLogicEvaluator();
        var disclosure = new DisclosureProcessor();
        var routing = new RoutingEngine(jsonLogic);
        return new ExecutionEngine(
            new ActionProcessor(schemaValidator, jsonLogic, disclosure, routing),
            schemaValidator, jsonLogic, disclosure, routing);
    }

    /// <summary>
    /// Action 0's two routes, copied from ce-uac-assessment-template.json action 0 (a real
    /// published blueprint), plus two downstream actions matching its "Issue Posture Credential"
    /// (id 1) and non-compliance-record (id 2) targets. Neither downstream action declares any
    /// routing of its own — a real leaf step — which is what exercises the "matched a TERMINAL
    /// route" vs "no routing configured at all" distinction this fix makes.
    /// </summary>
    private static BpModels.Blueprint AssessmentBlueprint() => new()
    {
        Id = BlueprintId,
        Title = "CE-UAC Assessment (fixture)",
        Description = "Routes copied from a real published blueprint (#1681)",
        Version = 1,
        Participants =
        [
            new BpModels.Participant { Id = "assessor", Name = "Assessor" },
        ],
        Actions =
        [
            new BpModels.Action
            {
                Id = 0,
                Title = "Assess UAC",
                Sender = "assessor",
                IsStartingAction = true,
                Routes =
                [
                    new BpModels.Route
                    {
                        Id = "compliant-issue",
                        NextActionIds = [1],
                        Condition = JsonNode.Parse(
                            """{ "==": [ { "var": "computedCompliant" }, true ] }"""),
                        Description = "UAC requirements met — proceed to issue the posture credential",
                    },
                    new BpModels.Route
                    {
                        Id = "noncompliant-record",
                        NextActionIds = [2],
                        IsDefault = true,
                        Description = "UAC requirements not met — record non-compliance, withhold the credential",
                    },
                ],
            },
            new BpModels.Action
            {
                Id = 1,
                Title = "Issue Posture Credential",
                Sender = "assessor",
            },
            new BpModels.Action
            {
                Id = 2,
                Title = "Record Non-Compliance",
                Sender = "assessor",
            },
        ],
    };

    private static Task<IResult> InvokeAsync(
        BpModels.Blueprint blueprint, Dictionary<string, object> data, string actionId = "0")
    {
        var blueprintStore = new Mock<IBlueprintStore>();
        blueprintStore.Setup(s => s.GetAsync(blueprint.Id)).ReturnsAsync(blueprint);

        var request = new RouteRequest { BlueprintId = blueprint.Id, ActionId = actionId, Data = data };

        return ExecutionRoutingEndpoint.HandleAsync(
            request, blueprintStore.Object, RealEngine(), NullLogger.Instance, CancellationToken.None);
    }

    private static object ReadOk(IResult result) =>
        result.Should().BeAssignableTo<IValueHttpResult>().Subject.Value!;

    private static List<object> NextActions(object value) =>
        ((IEnumerable)value.GetType().GetProperty("nextActions")!.GetValue(value)!)
            .Cast<object>()
            .ToList();

    private static T Prop<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    [Fact]
    public async Task Route_ConditionalRouteMatches_ReportsTheNextActionNotNoRouting()
    {
        var result = await InvokeAsync(
            AssessmentBlueprint(),
            new Dictionary<string, object> { ["computedCompliant"] = true });

        var value = ReadOk(result);
        var nextActions = NextActions(value);

        nextActions.Should().HaveCount(1,
            "the conditional route matched, so the simulator must report a next action rather than "
            + "an empty list — the #1681 defect was reporting empty here regardless of Routes");
        Prop<string>(nextActions[0], "actionId").Should().Be("1");
        Prop<string>(nextActions[0], "matchedRouteId").Should().Be("compliant-issue");
        Prop<bool>(value, "isWorkflowComplete").Should().BeFalse();
    }

    [Fact]
    public async Task Route_DefaultRouteMatches_ReportsTheNextAction()
    {
        // computedCompliant absent/false — falls through to the default route.
        var result = await InvokeAsync(AssessmentBlueprint(), new Dictionary<string, object>());

        var value = ReadOk(result);
        var nextActions = NextActions(value);

        nextActions.Should().HaveCount(1);
        Prop<string>(nextActions[0], "actionId").Should().Be("2");
        Prop<string>(nextActions[0], "matchedRouteId").Should().Be("noncompliant-record");
    }

    [Fact]
    public async Task Route_MatchedRouteIsTerminal_ReportsWorkflowCompleteWithMatchedRouteId()
    {
        // A route with an empty nextActionIds list is a DESIGNED end-of-workflow step, not an
        // authoring gap — the response must let a caller tell the two apart.
        var blueprint = AssessmentBlueprint();
        blueprint.Actions[0].Routes = new List<BpModels.Route>
        {
            new()
            {
                Id = "issued",
                NextActionIds = [],
                IsDefault = true,
                Description = "Posture credential issued — workflow complete",
            },
        };

        var result = await InvokeAsync(blueprint, new Dictionary<string, object>());

        var value = ReadOk(result);
        NextActions(value).Should().BeEmpty();
        Prop<bool>(value, "isWorkflowComplete").Should().BeTrue();
        Prop<string>(value, "matchedRouteId").Should().Be("issued",
            "a matched terminal route must still name itself — this is what distinguishes "
            + "\"reached the designed end\" from \"nothing is configured\"");
    }

    [Fact]
    public async Task Route_WalkingTwoStepsFromStart_ReachesTerminalSuccessNotNoRouting()
    {
        // #1681 acceptance criterion: a routes-based blueprint must be walkable, step by step, to a
        // terminal answer through THIS endpoint — the one the MCP simulator (and, before this fix,
        // nothing else) reported "No routing configured" for on every step. Action 1 here carries
        // its own terminal route, exactly like ce-uac-assessment-template.json's "issued" step.
        var blueprint = AssessmentBlueprint();
        blueprint.Actions[1].Routes =
        [
            new BpModels.Route
            {
                Id = "issued",
                NextActionIds = [],
                IsDefault = true,
                Description = "Posture credential issued — workflow complete",
            },
        ];

        // Step 1: action 0, conditional route matches → routes to action 1.
        var step1 = ReadOk(await InvokeAsync(
            blueprint, new Dictionary<string, object> { ["computedCompliant"] = true }, actionId: "0"));
        var step1Next = NextActions(step1);
        step1Next.Should().HaveCount(1);
        Prop<string>(step1Next[0], "actionId").Should().Be("1");

        // Step 2: action 1, its own default route is terminal → workflow complete, not "no routing".
        var step2 = ReadOk(await InvokeAsync(blueprint, new Dictionary<string, object>(), actionId: "1"));
        NextActions(step2).Should().BeEmpty();
        Prop<bool>(step2, "isWorkflowComplete").Should().BeTrue();
        Prop<string>(step2, "matchedRouteId").Should().Be("issued",
            "the walk reached a DESIGNED terminal step, which is what proves the gate this action "
            + "feeds (F142 rehearsal) is reachable rather than permanently blocked on a false "
            + "\"no routing configured\"");
    }

    [Fact]
    public async Task Route_ActionHasNoRoutingAtAll_ReportsEmptyWithoutAMatchedRouteId()
    {
        // Anti-vacuity: an action that genuinely has neither Routes nor legacy Participants
        // configured must still report "nothing matched" — matchedRouteId null distinguishes this
        // from the terminal-route case above.
        var blueprint = AssessmentBlueprint();
        blueprint.Actions[0].Routes = null;
        blueprint.Actions[0].Participants = null;

        var result = await InvokeAsync(blueprint, new Dictionary<string, object>());

        var value = ReadOk(result);
        NextActions(value).Should().BeEmpty();
        Prop<bool>(value, "isWorkflowComplete").Should().BeTrue();
        Prop<string?>(value, "matchedRouteId").Should().BeNull();
    }
}
