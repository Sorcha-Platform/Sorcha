// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Tests.Chat;

/// <summary>
/// Issue #1548 — the chat <c>validate_blueprint</c> tool reported VALID for a blueprint that
/// cannot execute (a starting action with no routes, every other route looping back to it), which
/// <c>POST /publish</c> then refused with a 400. The author's first sight of the problem was at
/// Go-live, and the model relayed "valid and ready to use" in the meantime.
/// </summary>
public class ValidateBlueprintReachabilityTests
{
    private static BlueprintToolExecutor Executor() => new(
        NullLogger<BlueprintToolExecutor>.Instance,
        new Mock<ISchemaIndexService>().Object,
        new Mock<IBlueprintTemplateService>().Object);

    private static async Task<string> ValidateAsync(BlueprintModel blueprint)
    {
        // BuildDraft() returns the builder's live blueprint, so seeding it here is equivalent to
        // having reached this state through the tools - and keeps this test independent of #1547.
        var builder = BlueprintBuilder.Create();
        var draft = builder.BuildDraft();
        draft.Id = blueprint.Id;
        draft.Title = blueprint.Title;
        draft.Description = blueprint.Description;
        draft.Metadata = blueprint.Metadata;
        draft.Participants = blueprint.Participants;
        draft.Actions = blueprint.Actions;

        using var args = JsonDocument.Parse("{}");
        var result = await Executor().ExecuteAsync("validate_blueprint", args, builder);
        result.Success.Should().BeTrue();
        return result.Result?.RootElement.ToString() ?? string.Empty;
    }

    private static Participant P(string id) => new() { Id = id, Name = id };

    private static BlueprintAction A(
        int id, string sender, bool starting = false,
        IEnumerable<Route>? routes = null, RejectionConfig? rejection = null) => new()
    {
        Id = id,
        Title = $"Action {id}",
        Sender = sender,
        IsStartingAction = starting,
        Disclosures = [new Disclosure(sender, ["/*"])],
        Routes = routes,
        RejectionConfig = rejection
    };

    private static BlueprintModel Bp(params BlueprintAction[] actions) => new()
    {
        Id = "bp",
        Title = "Reachability Blueprint",
        Description = "A blueprint used to exercise route reachability.",
        Participants = [P("alpha"), P("beta")],
        Actions = [.. actions]
    };

    private static Route To(params int[] next) => new() { Id = $"to-{string.Join('-', next)}", NextActionIds = next, IsDefault = true };
    private static Route Terminal() => new() { Id = "terminal", NextActionIds = [], IsDefault = true };

    // ---- the shape the designer actually produced -------------------------------------------

    [Fact]
    public async Task StartingActionWithNoRoutes_IsAnError()
    {
        // action 1 is the starting action and declares no routes; action 2 routes back to it.
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true),
            A(2, "beta", routes: [To(1)])));

        json.Should().Contain("STARTING_ACTION_NO_ROUTES",
            "a starting action with nowhere to go cannot advance the workflow — this is exactly " +
            "what the chat validator passed and /publish then refused");
        json.Should().Contain("\"isValid\":false");
    }

    [Fact]
    public async Task ActionUnreachableFromAnyStartingAction_IsAnError()
    {
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true, routes: [Terminal()]),
            A(2, "beta", routes: [Terminal()])));

        json.Should().Contain("UNREACHABLE_ACTION");
    }

    [Fact]
    public async Task EveryRouteLoopingToTheStart_LeavesNoTerminalPath()
    {
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true, routes: [To(2)]),
            A(2, "beta", routes: [To(1)])));

        json.Should().Contain("NO_TERMINAL_PATH");
    }

    [Fact]
    public async Task DuplicateParticipantIds_AreAnError()
    {
        var bp = Bp(A(1, "alpha", starting: true, routes: [Terminal()]));
        bp.Participants = [P("alpha"), P("beta"), P("alpha"), P("beta")];

        var json = await ValidateAsync(bp);

        json.Should().Contain("DUPLICATE_PARTICIPANT_ID",
            "two participants sharing an id cannot be disambiguated by action.sender");
    }

    // ---- the negatives, which matter more than the positives ---------------------------------

    [Fact]
    public async Task MultiActionBlueprintWithNoRoutesAtAll_IsAnError()
    {
        // ⚠ This test previously asserted the OPPOSITE — that a route-less multi-action blueprint
        // is "legacy" and must not be flagged. That assertion encoded a hole, found by a live
        // designer run: the model called no routing tool at all, the reachability gate skipped
        // every check, validate reported "no errors, no warnings", and the blueprint then
        // PUBLISHED (the publish path reports unreachability only as a warning). A workflow that
        // cannot advance past its starting action reached a register.
        //
        // Corpus-testing the rule against 45 shipped blueprints could not have caught this: none
        // of them had that shape. Only running the designer did.
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true),
            A(2, "beta"),
            A(3, "alpha")));

        json.Should().Contain("NO_ROUTING_DEFINED",
            "nothing can advance past the starting action, so the workflow cannot run");
        json.Should().Contain("\"isValid\":false");

        // The reachability GRAPH checks stay quiet — there is no graph to walk, and reporting
        // every action as unreachable would bury the one message that explains the problem.
        json.Should().NotContain("UNREACHABLE_ACTION");
        json.Should().NotContain("NO_TERMINAL_PATH");
    }

    [Fact]
    public async Task ParticipantRoutedBlueprint_IsNotFlagged()
    {
        // add_action's routeToNext populates Action.Participants (the legacy Condition model), not
        // Routes. The chat tools still offer it, so a blueprint routed that way is coherent and
        // must not be reported as having no routing.
        var bp = Bp(A(1, "alpha", starting: true), A(2, "beta"));
        bp.Actions[0].Participants = [new Condition("beta", true)];

        var json = await ValidateAsync(bp);

        json.Should().NotContain("NO_ROUTING_DEFINED");
    }

    [Fact]
    public async Task SingleActionBlueprintWithNoRoutes_IsNotFlagged()
    {
        // A one-action credential gate is legitimately terminal and needs no routes at all.
        var json = await ValidateAsync(Bp(A(1, "alpha", starting: true)));

        json.Should().NotContain("NO_ROUTING_DEFINED");
        json.Should().NotContain("UNREACHABLE_ACTION");
        json.Should().NotContain("NO_TERMINAL_PATH");
    }

    [Fact]
    public async Task PartiallyRoutedBlueprint_StillGetsTheGraphChecks()
    {
        // One route anywhere means the author IS using route-based routing, so the graph checks
        // apply and NO_ROUTING_DEFINED must not fire instead of them.
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true),
            A(2, "beta", routes: [Terminal()])));

        json.Should().NotContain("NO_ROUTING_DEFINED");
        json.Should().Contain("STARTING_ACTION_NO_ROUTES");
    }

    [Fact]
    public async Task SingleActionBlueprint_IsNotFlagged()
    {
        // A one-action credential gate is legitimately terminal.
        var json = await ValidateAsync(Bp(A(1, "alpha", starting: true, routes: [Terminal()])));

        json.Should().NotContain("STARTING_ACTION_NO_ROUTES");
        json.Should().NotContain("UNREACHABLE_ACTION");
        json.Should().NotContain("NO_TERMINAL_PATH");
    }

    [Fact]
    public async Task DeclaredCyclicBlueprint_IsExemptFromTheTerminalCheck()
    {
        var bp = Bp(
            A(1, "alpha", starting: true, routes: [To(2)]),
            A(2, "beta", routes: [To(1)]));
        bp.Metadata = new Dictionary<string, string> { ["hasCycles"] = "true" };

        var json = await ValidateAsync(bp);

        json.Should().NotContain("NO_TERMINAL_PATH",
            "a blueprint that declares its loop is intentional must not be nagged about it");
    }

    [Fact]
    public async Task RejectionTargetCountsAsReachability()
    {
        // Action 3 is reached only by a rejection bounce-back, never by a route.
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true, routes: [To(2)]),
            A(2, "beta", routes: [Terminal()], rejection: new RejectionConfig { TargetActionId = 3 }),
            A(3, "alpha", routes: [Terminal()])));

        json.Should().NotContain("UNREACHABLE_ACTION",
            "rejectionConfig.targetActionId is a real way to reach an action");
    }

    [Fact]
    public async Task AWellFormedTwoActionWorkflow_IsClean()
    {
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true, routes: [To(2)]),
            A(2, "beta", routes: [Terminal()])));

        json.Should().NotContain("STARTING_ACTION_NO_ROUTES");
        json.Should().NotContain("UNREACHABLE_ACTION");
        json.Should().NotContain("NO_TERMINAL_PATH");
        json.Should().NotContain("DUPLICATE_PARTICIPANT_ID");
    }
}
