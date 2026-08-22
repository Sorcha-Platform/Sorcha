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
    public async Task LegacyBlueprintWithNoRoutesAtAll_IsNotFlagged()
    {
        // complex-sme-invoice-finance and register-governance-v1 are advanced by other means and
        // declare no routes on any action. Flagging them would be a false positive, and the rule
        // is gated on the blueprint using route-based routing at all.
        var json = await ValidateAsync(Bp(
            A(1, "alpha", starting: true),
            A(2, "beta"),
            A(3, "alpha")));

        json.Should().NotContain("STARTING_ACTION_NO_ROUTES");
        json.Should().NotContain("UNREACHABLE_ACTION");
        json.Should().NotContain("NO_TERMINAL_PATH");
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
