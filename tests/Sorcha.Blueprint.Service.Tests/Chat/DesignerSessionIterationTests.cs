// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Chat;

/// <summary>
/// Issue #1547 — the designer chat rebuilds its <see cref="BlueprintBuilder"/> from the stored
/// session draft on <b>every</b> message. When that rebuild dropped actions, the second message in
/// a session saw an empty blueprint: <c>validate_blueprint</c> answered <c>MIN_ACTIONS</c>,
/// <c>set_action_metadata</c> answered "Action with ID 0 not found", and the model rebuilt from
/// scratch — so the designer could only ever produce one message's worth of blueprint.
/// </summary>
/// <remarks>
/// These tests exercise the message boundary, not the builder in isolation. A single assertion
/// that a <b>second</b> validate still sees the actions is what would have caught the original bug.
/// </remarks>
public class DesignerSessionIterationTests
{
    private static BlueprintToolExecutor Executor() => new(
        NullLogger<BlueprintToolExecutor>.Instance,
        new Mock<ISchemaIndexService>().Object,
        new Mock<IBlueprintTemplateService>().Object);

    /// <summary>Rebuilds the builder exactly as ChatOrchestrationService does between messages.</summary>
    private static BlueprintBuilder RebuildAsNextMessage(BlueprintModel draft)
    {
        var method = typeof(ChatOrchestrationService).GetMethod(
            "CreateBuilderFromBlueprint", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "ChatOrchestrationService.CreateBuilderFromBlueprint not found — has it been renamed?");

        return (BlueprintBuilder)method.Invoke(null, new object[] { draft })!;
    }

    private static async Task<string> RunToolAsync(string tool, string argsJson, BlueprintBuilder builder)
    {
        using var args = JsonDocument.Parse(argsJson);
        var result = await Executor().ExecuteAsync(tool, args, builder);
        result.Success.Should().BeTrue($"tool '{tool}' should execute: {result.Error}");
        return result.Result?.RootElement.ToString() ?? string.Empty;
    }

    /// <summary>Message 1: the model builds a two-participant, two-action workflow.</summary>
    private static async Task<BlueprintBuilder> FirstMessageAsync()
    {
        var builder = BlueprintBuilder.Create();
        await RunToolAsync("create_blueprint",
            """{"title":"Contractor Certification","description":"Trade body certifies a contractor."}""", builder);
        await RunToolAsync("add_participant",
            """{"id":"contractor","name":"Contractor"}""", builder);
        await RunToolAsync("add_participant",
            """{"id":"assessor","name":"Assessor"}""", builder);
        await RunToolAsync("add_action",
            """{"id":1,"title":"Apply","sender":"contractor","isStartingAction":true,"dataFields":[{"name":"companyName","type":"string"}]}""",
            builder);
        await RunToolAsync("add_action",
            """{"id":2,"title":"Assess","sender":"assessor","dataFields":[{"name":"decision","type":"string"}]}""",
            builder);
        return builder;
    }

    [Fact]
    public async Task SecondMessage_StillSeesTheActionsBuiltInTheFirst()
    {
        var draft = (await FirstMessageAsync()).BuildDraft();
        draft.Actions.Should().HaveCount(2, "sanity: the first message did build two actions");

        // --- message boundary: the session is stored, then a new builder is made from the draft
        var next = RebuildAsNextMessage(draft);

        next.BuildDraft().Actions.Should().HaveCount(2,
            "every message after the first rebuilds the builder from the stored draft; dropping " +
            "actions there is what made the designer unable to iterate (#1547)");
    }

    [Fact]
    public async Task SecondValidate_DoesNotReportMinActions()
    {
        var draft = (await FirstMessageAsync()).BuildDraft();

        // first validate, in the first message — this always passed
        var first = await RunToolAsync("validate_blueprint", "{}", RebuildAsNextMessage(draft));
        first.Should().NotContain("MIN_ACTIONS");

        // second validate, in a LATER message — this is the one that reported MIN_ACTIONS
        var second = await RunToolAsync("validate_blueprint", "{}", RebuildAsNextMessage(draft));
        second.Should().NotContain("MIN_ACTIONS",
            "the actions are still there; reporting MIN_ACTIONS is what provoked the model into " +
            "destructively rebuilding the blueprint from scratch");
    }

    [Fact]
    public async Task LaterMessage_CanStillAddressAnExistingActionById()
    {
        var draft = (await FirstMessageAsync()).BuildDraft();
        var next = RebuildAsNextMessage(draft);

        using var args = JsonDocument.Parse("""{"actionId":1,"participantId":"assessor","fields":["/*"]}""");
        var result = await Executor().ExecuteAsync("set_disclosure", args, next);

        result.Success.Should().BeTrue(
            "'Action with ID N not found' on a later message is the same defect seen from a " +
            "different tool: {0}", result.Error ?? "(no error)");
    }

    [Fact]
    public async Task RebuildingThenReAddingParticipants_DoesNotDuplicateThem()
    {
        // The live shape: the model believes the state is corrupt and re-runs its whole tool
        // sequence. Before #1549 that produced contractor, assessor, contractor, assessor.
        var draft = (await FirstMessageAsync()).BuildDraft();
        var next = RebuildAsNextMessage(draft);

        await RunToolAsync("add_participant", """{"id":"contractor","name":"Contractor"}""", next);
        await RunToolAsync("add_participant", """{"id":"assessor","name":"Assessor"}""", next);

        var rebuilt = next.BuildDraft();
        rebuilt.Participants.Should().HaveCount(2);
        rebuilt.Participants.Select(p => p.Id).Should().OnlyHaveUniqueItems(
            "duplicate participant ids cannot be disambiguated by action.sender (#1549)");
    }
}
