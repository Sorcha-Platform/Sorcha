// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Sorcha.UI.Components.User.Components.Onboarding;
using Sorcha.UI.Core.Tests.TestHosts;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Onboarding;

/// <summary>
/// bUnit tests for <see cref="GuidedTourScaffold"/>, guarding the reopen-loop fix:
/// after the user finishes or skips the tour, a parent re-render with
/// <see cref="GuidedTourScaffold.ShouldRun"/> still true (the host persists the
/// dismissal flag asynchronously) must NOT re-open the tour at step 1. Lowering
/// then raising ShouldRun (Settings → Replay tour) must still re-run it.
/// </summary>
/// <remarks>
/// Rendered via <see cref="GuidedTourHost"/> because the scaffold's step content
/// renders through an inline MudDialog, which needs MudBlazor's popover/dialog
/// providers in the tree.
/// </remarks>
public sealed class GuidedTourScaffoldTests : ComponentTestFixture
{
    private static readonly IReadOnlyList<GuidedTourScaffold.GuidedTourStep> TwoSteps = new[]
    {
        new GuidedTourScaffold.GuidedTourStep("First step", "Step one body"),
        new GuidedTourScaffold.GuidedTourStep("Second step", "Step two body"),
    };

    [Fact]
    public void Opens_WhenShouldRunTrue()
    {
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true));

        // MudDialog open/close is async (popover render), so settle with WaitForAssertion.
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("First step", "the tour opens at step 1 when ShouldRun is true"));
    }

    [Fact]
    public void DoesNotOpen_WhenShouldRunFalse()
    {
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, false));

        cut.Markup.Should().NotContain("First step");
    }

    [Fact]
    public void DoesNotReopen_AfterDone_WhenShouldRunStillTrue()
    {
        var completed = 0;
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true)
            .Add(p => p.OnCompleted, EventCallback.Factory.Create(this, () => completed++)));

        cut.WaitForAssertion(() => cut.Find("[data-testid='guided-tour-next']"));
        cut.Find("[data-testid='guided-tour-next']").Click();   // step 1 → step 2 (last)
        cut.Find("[data-testid='guided-tour-done']").Click();   // complete

        completed.Should().Be(1);

        // The host persists the dismissal flag asynchronously, so a re-render lands
        // with ShouldRun STILL true. The one-shot latch must keep the tour closed —
        // previously this re-opened it at step 1 in a loop the user couldn't escape.
        cut.Render(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true));

        cut.WaitForAssertion(() =>
            cut.Markup.Should().NotContain("First step", "the tour must not re-assert itself after completion"));
    }

    [Fact]
    public void DoesNotReopen_AfterSkip_WhenShouldRunStillTrue()
    {
        var dismissed = 0;
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true)
            .Add(p => p.OnDismissedEarly, EventCallback.Factory.Create(this, () => dismissed++)));

        // Advance to step 2 first, then skip. A reopen would reset to step 1, so
        // "First step" reappearing is the unambiguous reopen signal — MudDialog's
        // popover DOM isn't torn down on close in bUnit (JS no-op), so "dialog gone"
        // isn't observable; "content reset to step 1" is.
        cut.WaitForAssertion(() => cut.Find("[data-testid='guided-tour-next']"));
        cut.Find("[data-testid='guided-tour-next']").Click();    // step 1 → step 2
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Second step"));
        cut.Find("[data-testid='guided-tour-skip']").Click();
        dismissed.Should().Be(1);

        cut.Render(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true));

        cut.Markup.Should().NotContain("First step",
            "skipping must not re-assert the tour at step 1 on the next re-render");
    }

    [Fact]
    public void Advances_ThroughSteps_ToDone()
    {
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("First step"));
        cut.Find("[data-testid='guided-tour-next']").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Second step", "Next advances to step 2"));
        cut.Find("[data-testid='guided-tour-done']").Should().NotBeNull("the last step shows Done, not Next");
    }

    [Fact]
    public void ReRuns_WhenShouldRunLoweredThenRaised()
    {
        var cut = Render<GuidedTourHost>(ps => ps
            .Add(p => p.Steps, TwoSteps)
            .Add(p => p.ShouldRun, true));

        // Advance to step 2 then skip, so the residual popover content is step 2 — a
        // re-run resetting to step 1 ("First step") is then a genuine signal, not leftover.
        cut.WaitForAssertion(() => cut.Find("[data-testid='guided-tour-next']"));
        cut.Find("[data-testid='guided-tour-next']").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Second step"));
        cut.Find("[data-testid='guided-tour-skip']").Click();

        // Host lowers the flag (resets the latch) then raises it again — e.g. the
        // citizen taps Settings → Replay tour. The tour must run a second time at step 1.
        cut.Render(ps => ps.Add(p => p.Steps, TwoSteps).Add(p => p.ShouldRun, false));
        cut.Render(ps => ps.Add(p => p.Steps, TwoSteps).Add(p => p.ShouldRun, true));

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("First step", "lowering then raising ShouldRun re-runs the tour from step 1"));
    }
}
