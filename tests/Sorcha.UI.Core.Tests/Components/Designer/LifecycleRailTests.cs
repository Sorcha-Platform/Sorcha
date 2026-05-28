// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Designer;
using Sorcha.UI.Core.Models.Chat;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Tests.Components.Designer;

/// <summary>
/// bUnit tests for the Feature 142 <see cref="LifecycleRail"/> component covering stage rendering,
/// the Go-live gating lock (FR-002/FR-004), the Rehearse availability gate (FR-001), and navigation
/// to available stages via <see cref="DesignerContext.SetStage"/>.
/// </summary>
public class LifecycleRailTests : BunitContext
{
    public LifecycleRailTests()
    {
        Services.AddMudServices();
        Services.AddScoped<DesignerContext>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Renders the rail alongside a <see cref="MudPopoverProvider"/> sibling — MudTooltip renders
    /// through a popover that requires the provider in the render tree. Element queries on the returned
    /// fragment still see the rail's markup.
    /// </summary>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderRail() => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<LifecycleRail>(1);
        builder.CloseComponent();
    });

    private static BlueprintModel ValidBlueprint() => new()
    {
        Title = "Test Blueprint",
        Description = "A blueprint used for lifecycle rail gating tests.",
    };

    private static ValidationResult NoErrors() => new() { IsValid = true };

    /// <summary>All four stage anchors render with their exact data-testids.</summary>
    [Fact]
    public void Render_AllFourStages_RenderWithTestIds()
    {
        var cut = RenderRail();

        cut.Find("[data-testid='lifecycle-rail']").Should().NotBeNull();
        cut.Find("[data-testid='rail-stage-describe']").Should().NotBeNull();
        cut.Find("[data-testid='rail-stage-understand']").Should().NotBeNull();
        cut.Find("[data-testid='rail-stage-rehearse']").Should().NotBeNull();
        cut.Find("[data-testid='rail-stage-golive']").Should().NotBeNull();
    }

    /// <summary>Go live is locked (lock indicator present) when no rehearsal has passed for the current exec def.</summary>
    [Fact]
    public void GoLive_NoRehearsalPassed_ShowsLockIndicator()
    {
        var cut = RenderRail();

        cut.FindAll("[data-testid='rail-golive-lock']").Should().HaveCount(1,
            "Go live must be locked until a full rehearsal passes");
    }

    /// <summary>With a valid blueprint and a no-errors validation result, the Rehearse stage is available (not locked).</summary>
    [Fact]
    public void Rehearse_ValidBlueprintNoErrors_IsAvailable()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(ValidBlueprint());
        ctx.UpdateValidation(NoErrors());

        var cut = RenderRail();

        var rehearse = cut.Find("[data-testid='rail-stage-rehearse']");
        rehearse.GetAttribute("data-stage-state").Should().NotBe("locked",
            "a valid blueprint with no blocking errors unlocks Rehearse");
    }

    /// <summary>Rehearse stays locked when validation has blocking errors.</summary>
    [Fact]
    public void Rehearse_BlueprintWithValidationErrors_IsLocked()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(ValidBlueprint());
        ctx.UpdateValidation(new ValidationResult
        {
            IsValid = false,
            Errors =
            [
                new ValidationError { Code = "MIN_PARTICIPANTS", Message = "Need at least two participants." },
            ],
        });

        var cut = RenderRail();

        cut.Find("[data-testid='rail-stage-rehearse']").GetAttribute("data-stage-state")
            .Should().Be("locked", "blocking errors keep Rehearse locked");
    }

    /// <summary>Once a rehearsal passes for the current exec def, the Go-live lock indicator is gone.</summary>
    [Fact]
    public void GoLive_RehearsalPassed_LockIndicatorAbsent()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(ValidBlueprint());
        ctx.RecordRehearsalPassed();

        var cut = RenderRail();

        cut.FindAll("[data-testid='rail-golive-lock']").Should().BeEmpty(
            "Go live unlocks once a full rehearsal passes for the current executable definition");
    }

    /// <summary>Clicking an available stage navigates the lifecycle (SetStage effect).</summary>
    [Fact]
    public void Click_AvailableStage_ChangesCurrentStage()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        ctx.SetBlueprint(ValidBlueprint());
        ctx.UpdateValidation(NoErrors());

        var cut = RenderRail();
        cut.Find("[data-testid='rail-stage-understand']").Click();

        ctx.Lifecycle.CurrentStage.Should().Be(LifecycleStage.Understand,
            "clicking an available stage should move the lifecycle to that stage");
    }

    /// <summary>Clicking a locked stage does not navigate.</summary>
    [Fact]
    public void Click_LockedStage_DoesNotChangeCurrentStage()
    {
        var ctx = Services.GetRequiredService<DesignerContext>();
        // No blueprint => Understand/Rehearse/GoLive all locked.

        var cut = RenderRail();
        cut.Find("[data-testid='rail-stage-golive']").Click();

        ctx.Lifecycle.CurrentStage.Should().Be(LifecycleStage.Describe,
            "a locked stage must not be navigable");
    }
}
