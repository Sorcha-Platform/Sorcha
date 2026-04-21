// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Chat;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Tests.Services.Designer;

public class DesignerContextTests
{
    private static BlueprintModel NewBlueprint(string title = "BP") => new() { Title = title };

    private static (DesignerContext ctx, Func<int> count) CreateTracked()
    {
        var ctx = new DesignerContext();
        var count = 0;
        ctx.Changed += () => count++;
        return (ctx, () => count);
    }

    [Fact]
    public void InitialState_HasExpectedDefaults()
    {
        var ctx = new DesignerContext();

        ctx.Blueprint.Should().BeNull();
        ctx.Validation.Should().BeNull();
        ctx.ChatSessionId.Should().BeNull();
        ctx.ActiveActionId.Should().BeNull();
        ctx.IsManualCursor.Should().BeFalse();
        ctx.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void SetBlueprint_DoesNotSetIsDirty()
    {
        var (ctx, count) = CreateTracked();

        ctx.SetBlueprint(NewBlueprint());

        ctx.Blueprint.Should().NotBeNull();
        ctx.IsDirty.Should().BeFalse();
        count().Should().Be(1);
    }

    [Fact]
    public void SetBlueprint_ResetsActiveActionAndManualCursor()
    {
        var ctx = new DesignerContext();
        ctx.SetActiveActionManual("7");
        ctx.IsManualCursor.Should().BeTrue();

        ctx.SetBlueprint(NewBlueprint());

        ctx.ActiveActionId.Should().BeNull();
        ctx.IsManualCursor.Should().BeFalse();
    }

    [Fact]
    public void ApplyAiUpdate_AutoCursors_WhenManualCursorIsFalse()
    {
        var (ctx, count) = CreateTracked();

        ctx.ApplyAiUpdate(NewBlueprint(), new ValidationResult { IsValid = true }, editedActionId: "3");

        ctx.ActiveActionId.Should().Be("3");
        ctx.IsDirty.Should().BeTrue();
        count().Should().Be(1);
    }

    [Fact]
    public void ApplyAiUpdate_PreservesCursor_WhenManualCursorIsTrue_ButStillTracks()
    {
        var ctx = new DesignerContext();
        ctx.SetActiveActionManual("1");

        ctx.ApplyAiUpdate(NewBlueprint(), null, editedActionId: "5");

        ctx.ActiveActionId.Should().Be("1");
        ctx.IsManualCursor.Should().BeTrue();

        // FollowAi roundtrip proves _lastAiEditedActionId was updated to "5".
        ctx.FollowAi();
        ctx.ActiveActionId.Should().Be("5");
        ctx.IsManualCursor.Should().BeFalse();
    }

    [Fact]
    public void SetActiveActionManual_FlipsManualFlag()
    {
        var (ctx, count) = CreateTracked();

        ctx.SetActiveActionManual("2");

        ctx.ActiveActionId.Should().Be("2");
        ctx.IsManualCursor.Should().BeTrue();
        count().Should().Be(1);
    }

    [Fact]
    public void FollowAi_ReleasesManualCursorAndJumpsToLastAiEdit()
    {
        var ctx = new DesignerContext();
        ctx.ApplyAiUpdate(NewBlueprint(), null, "9");
        ctx.SetActiveActionManual("1");

        ctx.FollowAi();

        ctx.IsManualCursor.Should().BeFalse();
        ctx.ActiveActionId.Should().Be("9");
    }

    [Fact]
    public void FollowAi_WhenNoAiEditYet_ClearsActiveAction()
    {
        var ctx = new DesignerContext();
        ctx.SetActiveActionManual("1");

        ctx.FollowAi();

        ctx.ActiveActionId.Should().BeNull();
        ctx.IsManualCursor.Should().BeFalse();
    }

    [Fact]
    public void MarkDirty_Transitions_FireChangedOnce()
    {
        var (ctx, count) = CreateTracked();

        ctx.MarkDirty();

        ctx.IsDirty.Should().BeTrue();
        count().Should().Be(1);
    }

    [Fact]
    public void MarkDirty_AlreadyDirty_IsIdempotentAndDoesNotFire()
    {
        var ctx = new DesignerContext();
        ctx.MarkDirty();
        var count = 0;
        ctx.Changed += () => count++;

        ctx.MarkDirty();

        ctx.IsDirty.Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public void MarkClean_Transitions_FireChangedOnce()
    {
        var ctx = new DesignerContext();
        ctx.MarkDirty();
        var count = 0;
        ctx.Changed += () => count++;

        ctx.MarkClean();

        ctx.IsDirty.Should().BeFalse();
        count.Should().Be(1);
    }

    [Fact]
    public void MarkClean_AlreadyClean_IsIdempotentAndDoesNotFire()
    {
        var (ctx, count) = CreateTracked();

        ctx.MarkClean();

        ctx.IsDirty.Should().BeFalse();
        count().Should().Be(0);
    }

    [Fact]
    public void UpdateValidation_FiresChangedOnce()
    {
        var (ctx, count) = CreateTracked();

        ctx.UpdateValidation(new ValidationResult { IsValid = false });

        ctx.Validation.Should().NotBeNull();
        count().Should().Be(1);
    }

    [Fact]
    public void EachPublicMutation_FiresChangedExactlyOnce()
    {
        var ctx = new DesignerContext();
        var count = 0;
        ctx.Changed += () => count++;

        ctx.SetBlueprint(NewBlueprint());                                      // 1
        ctx.ApplyAiUpdate(NewBlueprint(), null, "1");                          // 2
        ctx.SetActiveActionManual("2");                                        // 3
        ctx.FollowAi();                                                        // 4
        ctx.UpdateValidation(new ValidationResult { IsValid = true });                       // 5
        ctx.MarkClean();                                                       // 6 (was dirty from ApplyAiUpdate)
        ctx.MarkDirty();                                                       // 7

        count.Should().Be(7);
    }
}
