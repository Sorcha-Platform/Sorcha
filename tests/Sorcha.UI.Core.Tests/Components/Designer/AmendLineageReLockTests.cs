// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Tests.Components.Designer;

/// <summary>
/// Feature 142 (T057 / US6) — re-lock invariant for the amend / clone-from-published flow.
/// Loading an amend draft (a fresh Blueprint produced by <c>POST /api/blueprints/from-published</c>)
/// MUST leave Go live locked: the new draft has a different executable-definition hash from the
/// previously-rehearsed source, so <c>RehearsalPassedForCurrentExecDef</c> stays false until a
/// fresh full rehearsal is recorded on the amend draft.
/// </summary>
/// <remarks>
/// This is the FR-029/030 invariant. The test exercises it through <c>DesignerContext.SetBlueprint</c>
/// (which recomputes the hash on every Blueprint change) so the rail-driven Go-live lock is provable
/// without any UI rendering — the lock simply mirrors <c>RehearsalPassedForCurrentExecDef</c>.
/// </remarks>
public class AmendLineageReLockTests
{
    private static BlueprintModel TwoStepBlueprint(string title, string? actionTitleSuffix = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = title,
        Description = "Two-step blueprint used for the re-lock invariant test.",
        Participants =
        {
            new() { Id = "applicant", Name = "Applicant" },
            new() { Id = "reviewer", Name = "Reviewer" },
        },
        Actions =
        {
            new()
            {
                Id = 0,
                Title = $"Submit{actionTitleSuffix}",
                Sender = "applicant",
                IsStartingAction = true,
            },
            new()
            {
                Id = 1,
                Title = $"Review{actionTitleSuffix}",
                Sender = "reviewer",
            },
        },
    };

    [Fact]
    public void LoadingAmendDraft_AfterPassedRehearsal_ReLocksGoLive()
    {
        var context = new DesignerContext();

        // 1) Adopt the source-shaped blueprint and simulate a passing full rehearsal — Go live opens.
        var source = TwoStepBlueprint("Source service");
        context.SetBlueprint(source);
        context.RecordRehearsalPassed();
        context.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeTrue(
            "the recorded rehearsal pass mirrors the current exec-def hash → Go live should be unlocked.");

        // 2) Load an amend draft (different actionable structure ⇒ different exec-def hash).
        var amendDraft = TwoStepBlueprint("Amend draft", actionTitleSuffix: " v2");
        amendDraft.Metadata = new Dictionary<string, string>
        {
            ["x-source-register"] = "reg-source-1",
            ["x-source-blueprint-id"] = source.Id,
            ["x-source-version"] = "1",
        };

        context.SetBlueprint(amendDraft);

        // 3) The new exec-def hash differs from the previously-passed one ⇒ Go live is RE-LOCKED.
        context.Lifecycle.ExecDefHash.Should().NotBeNullOrEmpty();
        context.Lifecycle.PassedExecDefHash.Should().NotBe(context.Lifecycle.ExecDefHash,
            "the amend draft has a fresh exec-def hash that no RehearsalPass yet matches.");
        context.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeFalse(
            "loading an amend draft MUST re-lock Go live until a fresh full rehearsal passes (FR-029/FR-030).");
    }

    [Fact]
    public void AmendContext_IsAuthoredFromLineageMetadata()
    {
        // The lineage record itself is a simple value type; this test pins the record shape so any
        // change to the AmendContext contract surfaces here alongside the rehydration logic in
        // DesignerBlueprint.razor.cs (which reads the same three metadata keys).
        var amend = new AmendContext("reg-source-1", "bp-source-abc", 7);

        amend.RegisterId.Should().Be("reg-source-1");
        amend.SourceBlueprintId.Should().Be("bp-source-abc");
        amend.SourceVersion.Should().Be(7);
    }
}
