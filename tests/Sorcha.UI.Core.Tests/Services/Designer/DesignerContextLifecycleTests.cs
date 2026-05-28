// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Services.Designer;

/// <summary>
/// Tests for the Feature 142 (T006) lifecycle additions to <see cref="DesignerContext"/>:
/// the <see cref="LifecycleState"/> instance, client-side exec-def hash recomputation on
/// Blueprint change, and the rehearsal-pass re-lock behaviour (FR-023) that the Go-live
/// UI lock mirrors.
/// </summary>
public class DesignerContextLifecycleTests
{
    private static BlueprintModel BlueprintWith(int actionCount)
    {
        var actions = new List<ActionModel>();
        for (var i = 1; i <= actionCount; i++)
        {
            actions.Add(new ActionModel
            {
                Id = i,
                Title = $"Action {i}",
                Sender = "applicant",
                IsStartingAction = i == 1,
            });
        }

        return new BlueprintModel
        {
            Id = "bp-lifecycle",
            Title = "Lifecycle Blueprint",
            Version = 1,
            Participants = [new Participant { Id = "applicant", Name = "Applicant" }],
            Actions = actions,
        };
    }

    [Fact]
    public void Initial_LifecycleState_HasExpectedDefaults()
    {
        var ctx = new DesignerContext();

        ctx.Lifecycle.Should().NotBeNull();
        ctx.Lifecycle.CurrentStage.Should().Be(LifecycleStage.Describe);
        ctx.Lifecycle.ExecDefHash.Should().BeNull();
        ctx.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeFalse();
        ctx.Lifecycle.AmendContext.Should().BeNull();
    }

    [Fact]
    public void SetBlueprint_RecomputesExecDefHash()
    {
        var ctx = new DesignerContext();

        ctx.SetBlueprint(BlueprintWith(1));

        ctx.Lifecycle.ExecDefHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RecordRehearsalPassed_MarksCurrentExecDefPassed()
    {
        var ctx = new DesignerContext();
        ctx.SetBlueprint(BlueprintWith(1));

        ctx.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeFalse();

        ctx.RecordRehearsalPassed();

        ctx.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeTrue();
    }

    [Fact]
    public void ReSettingEquivalentBlueprint_KeepsRehearsalPassed()
    {
        var ctx = new DesignerContext();
        ctx.SetBlueprint(BlueprintWith(1));
        ctx.RecordRehearsalPassed();

        // A structurally-identical blueprint hashes the same → pass survives.
        ctx.SetBlueprint(BlueprintWith(1));

        ctx.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeTrue();
    }

    [Fact]
    public void StructuralChange_RelocksRehearsal()
    {
        var ctx = new DesignerContext();
        ctx.SetBlueprint(BlueprintWith(1));
        ctx.RecordRehearsalPassed();

        // Adding an action changes the executable definition → re-lock (FR-023).
        ctx.SetBlueprint(BlueprintWith(2));

        ctx.Lifecycle.RehearsalPassedForCurrentExecDef.Should().BeFalse();
    }

    [Fact]
    public void SetStage_UpdatesStageAndFiresChanged()
    {
        var ctx = new DesignerContext();
        var fired = 0;
        ctx.Changed += () => fired++;

        ctx.SetStage(LifecycleStage.Rehearse);

        ctx.Lifecycle.CurrentStage.Should().Be(LifecycleStage.Rehearse);
        fired.Should().Be(1);
    }
}
