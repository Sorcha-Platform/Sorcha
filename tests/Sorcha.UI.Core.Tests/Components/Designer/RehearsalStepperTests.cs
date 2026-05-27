// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Designer;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Designer;

/// <summary>
/// bUnit tests for the Feature 142 shared <see cref="RehearsalStepper"/> (T025): step + role-switcher
/// + log rendering, the credential "checked in full rehearsal" note, and that switching role /
/// submitting / resetting invoke the supplied callbacks. The component is backend-agnostic — these
/// tests drive it purely through its <c>RehearsalStepView</c> / <c>RehearsalLogView</c> parameters.
/// </summary>
public class RehearsalStepperTests : BunitContext
{
    public RehearsalStepperTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Stepper parameters captured by <see cref="RenderStepper"/> so they can be applied inside a
    /// render fragment that also mounts a <see cref="MudPopoverProvider"/> sibling — MudSelect
    /// renders its dropdown through a popover that requires the provider in the render tree.
    /// </summary>
    private sealed record StepperParams(
        IReadOnlyList<RehearsalStepView> Steps,
        IReadOnlyList<string> Roles,
        IReadOnlyList<RehearsalLogView> Log,
        string? CurrentRole,
        bool IsComplete,
        EventCallback<string> OnSwitchRole,
        EventCallback OnSubmitStep,
        EventCallback OnReset);

    private static IReadOnlyList<RehearsalStepView> ThreeSteps() =>
    [
        new RehearsalStepView(1, "Apply", "applicant", RehearsalStepViewStatus.Done,
            RoutingSummary: "Routed to action 2 (reviewer)."),
        new RehearsalStepView(2, "Review", "reviewer", RehearsalStepViewStatus.Current),
        new RehearsalStepView(3, "Issue badge", "issuer", RehearsalStepViewStatus.Pending,
            CredentialNote: "Credential issuance is checked in full rehearsal."),
    ];

    private static IReadOnlyList<string> Roles() => ["applicant", "reviewer", "issuer"];

    private static IReadOnlyList<RehearsalLogView> Log() =>
    [
        new RehearsalLogView("applicant submitted \"Apply\".", RehearsalLogKind.Info),
        new RehearsalLogView("Routed to action 2 (reviewer).", RehearsalLogKind.Routed),
    ];

    /// <summary>
    /// Renders the stepper alongside a <see cref="MudPopoverProvider"/> sibling (required for the
    /// MudSelect role-switcher's popover) and returns the typed <see cref="RehearsalStepper"/>.
    /// </summary>
    private IRenderedComponent<RehearsalStepper> RenderStepper(
        IReadOnlyList<RehearsalStepView>? steps = null,
        IReadOnlyList<string>? roles = null,
        IReadOnlyList<RehearsalLogView>? log = null,
        string? currentRole = "reviewer",
        bool isComplete = false,
        EventCallback<string>? onSwitchRole = null,
        EventCallback? onSubmitStep = null,
        EventCallback? onReset = null)
    {
        var p = new StepperParams(
            steps ?? ThreeSteps(),
            roles ?? Roles(),
            log ?? Log(),
            currentRole,
            isComplete,
            onSwitchRole ?? default,
            onSubmitStep ?? default,
            onReset ?? default);

        var container = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<RehearsalStepper>(1);
            builder.AddAttribute(2, nameof(RehearsalStepper.Steps), p.Steps);
            builder.AddAttribute(3, nameof(RehearsalStepper.Roles), p.Roles);
            builder.AddAttribute(4, nameof(RehearsalStepper.Log), p.Log);
            builder.AddAttribute(5, nameof(RehearsalStepper.CurrentActingRole), p.CurrentRole);
            builder.AddAttribute(6, nameof(RehearsalStepper.IsComplete), p.IsComplete);
            builder.AddAttribute(7, nameof(RehearsalStepper.OnSwitchRole), p.OnSwitchRole);
            builder.AddAttribute(8, nameof(RehearsalStepper.OnSubmitStep), p.OnSubmitStep);
            builder.AddAttribute(9, nameof(RehearsalStepper.OnReset), p.OnReset);
            builder.CloseComponent();
        });

        return container.FindComponent<RehearsalStepper>();
    }

    [Fact]
    public void Render_StepsRoleSwitcherAndLog_AllPresent()
    {
        var cut = RenderStepper();

        cut.Find("[data-testid='rehearsal-stepper']").Should().NotBeNull();
        cut.Find("[data-testid='rehearsal-role-switcher']").Should().NotBeNull();
        cut.Find("[data-testid='rehearsal-log']").Should().NotBeNull();
        cut.Find("[data-testid='rehearsal-step-1']").Should().NotBeNull();
        cut.Find("[data-testid='rehearsal-step-2']").Should().NotBeNull();
        cut.Find("[data-testid='rehearsal-step-3']").Should().NotBeNull();
    }

    [Fact]
    public void Render_CredentialStep_ShowsCheckedInFullRehearsalNote()
    {
        var cut = RenderStepper();

        var note = cut.Find("[data-testid='rehearsal-step-3-cred-note']");
        note.TextContent.Should().Contain("full rehearsal");
    }

    [Fact]
    public void Render_NonCredentialStep_HasNoCredentialNote()
    {
        var cut = RenderStepper();

        cut.FindAll("[data-testid='rehearsal-step-1-cred-note']").Should().BeEmpty();
    }

    [Fact]
    public void Render_Log_ShowsEntries()
    {
        var cut = RenderStepper();

        cut.FindAll("[data-testid='rehearsal-log-entry']").Should().HaveCount(2);
        cut.Find("[data-testid='rehearsal-log']").TextContent.Should().Contain("Routed to action 2");
    }

    [Fact]
    public void Submit_CurrentStepPresent_InvokesOnSubmitStep()
    {
        var invoked = false;
        var cut = RenderStepper(onSubmitStep: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='rehearsal-step-submit']").Click();

        invoked.Should().BeTrue("submitting the current step must raise OnSubmitStep");
    }

    [Fact]
    public void Reset_Clicked_InvokesOnReset()
    {
        var invoked = false;
        var cut = RenderStepper(onReset: EventCallback.Factory.Create(this, () => invoked = true));

        cut.Find("[data-testid='rehearsal-reset']").Click();

        invoked.Should().BeTrue("clicking Reset must raise OnReset");
    }

    [Fact]
    public void SwitchRole_DifferentRole_InvokesOnSwitchRole()
    {
        string? switchedTo = null;
        var cut = RenderStepper(onSwitchRole: EventCallback.Factory.Create<string>(this, r => switchedTo = r));

        // The MudSelect exposes ValueChanged; invoke it directly to assert the callback wiring
        // (the dropdown's popover rendering is exercised by the E2E).
        var select = cut.FindComponent<MudSelect<string>>();
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("issuer"));

        switchedTo.Should().Be("issuer", "selecting a different role must raise OnSwitchRole with that role");
    }

    [Fact]
    public void SwitchRole_SameRole_DoesNotInvokeOnSwitchRole()
    {
        var invoked = false;
        var cut = RenderStepper(
            currentRole: "reviewer",
            onSwitchRole: EventCallback.Factory.Create<string>(this, _ => invoked = true));

        var select = cut.FindComponent<MudSelect<string>>();
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("reviewer"));

        invoked.Should().BeFalse("re-selecting the current role must not raise OnSwitchRole");
    }

    [Fact]
    public void Complete_NoCurrentStep_ShowsCompletionAndHidesSubmit()
    {
        var doneSteps = new List<RehearsalStepView>
        {
            new(1, "Apply", "applicant", RehearsalStepViewStatus.Done),
            new(2, "Review", "reviewer", RehearsalStepViewStatus.Done),
        };

        var cut = RenderStepper(steps: doneSteps, isComplete: true);

        cut.FindAll("[data-testid='rehearsal-step-submit']").Should().BeEmpty();
        cut.Find("[data-testid='rehearsal-complete']").Should().NotBeNull();
    }
}
