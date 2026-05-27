// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// E2E tests for the Feature 142 Rehearse stage (T021). With a smoke blueprint loaded at
/// <c>?stage=rehearse</c>, covers the dry-run / full mode pills, the in-WASM dry-run stepper +
/// role-switcher + step/reset affordances, and the full-rehearsal sandbox banner + start button.
/// </summary>
/// <remarks>
/// The dry-run harness runs entirely in WASM and needs no backend, so its surfaces are asserted
/// for real. The deep, timing-sensitive full-rehearsal walk (role switches, waiting for sandbox
/// seals) is validated through the backend API and is intentionally NOT driven here — these tests
/// assert that the full-mode entry surfaces render. A blueprint is provisioned via the REST API
/// (see <see cref="DesignerSuiteBase.CreateSmokeBlueprintAsync"/>); if the live stack rejects that
/// provisioning the test is inconclusive rather than a false failure.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Designer")]
[Category("Rehearsal")]
[Category("Authenticated")]
public class RehearsalTests : DesignerSuiteBase
{
    [Test]
    [Retry(2)]
    public async Task Rehearse_ModePills_RenderWithDryRunDefault()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "rehearse");

        Assert.That(await Designer.StageRehearse.IsVisibleAsync(), Is.True,
            "?stage=rehearse should render the Rehearse stage canvas.");

        var dryRunCount = await Designer.RehearseModeDryRun.CountAsync();
        var fullCount = await Designer.RehearseModeFull.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dryRunCount, Is.GreaterThan(0), "rehearse-mode-dryrun pill should render.");
            Assert.That(fullCount, Is.GreaterThan(0), "rehearse-mode-full pill should render.");
        });

        // Dry-run is the default mode: the in-WASM stepper (no backend) is shown, and the full-mode
        // sandbox banner is NOT.
        Assert.That(await Designer.RehearsalStepper.IsVisibleAsync(), Is.True,
            "Dry-run is the default mode, so the rehearsal stepper should be visible without switching modes.");
        Assert.That(await Designer.RehearseSandboxBanner.CountAsync(), Is.EqualTo(0),
            "The full-rehearsal sandbox banner should be absent while in the default dry-run mode.");
    }

    [Test]
    [Retry(2)]
    public async Task Rehearse_DryRun_StepperAndAffordancesPresent()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "rehearse");

        // The in-WASM dry-run stepper renders for a loaded blueprint with the role switcher, the
        // activity log, and the reset affordance — all backend-free.
        Assert.That(await Designer.RehearsalStepper.IsVisibleAsync(), Is.True,
            "The dry-run stepper should render for a loaded blueprint (in-WASM harness needs no backend).");

        var roleSwitcherCount = await Designer.RehearsalRoleSwitcher.CountAsync();
        var logCount = await Designer.RehearsalLog.CountAsync();
        var resetCount = await Designer.RehearsalReset.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(roleSwitcherCount, Is.GreaterThan(0),
                "The role-switcher should be present on the dry-run stepper.");
            Assert.That(logCount, Is.GreaterThan(0),
                "The plain-language activity log should be present on the dry-run stepper.");
            Assert.That(resetCount, Is.GreaterThan(0),
                "The reset affordance should be present on the dry-run stepper.");
        });

        // The submit affordance only renders once the dry-run harness has started with a current
        // step. Auto-start happens on init only if the blueprint was already loaded; when the
        // blueprint loads asynchronously after init, a deterministic re-entry to dry-run mode (toggle
        // out to Full and back) re-runs EnsureDryRunStarted so the starting action becomes current.
        if (await Designer.RehearsalStepSubmit.CountAsync() == 0)
        {
            await Designer.RehearseModeFull.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
            await Designer.RehearseModeDryRun.ClickAsync();
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        }

        Assert.That(await Designer.RehearsalStepSubmit.CountAsync(), Is.GreaterThan(0),
            "With the starting action current, the submit-step affordance should be present in the dry-run.");
    }

    [Test]
    [Retry(2)]
    public async Task Rehearse_FullMode_ShowsSandboxBannerAndStart()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "rehearse");

        // Switch to the full-rehearsal mode. The mode pill is a MudButton; click it directly.
        await Designer.RehearseModeFull.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        Assert.That(await Designer.RehearseSandboxBanner.IsVisibleAsync(), Is.True,
            "Switching to full rehearsal should reveal the private-sandbox banner.");

        var bannerText = await Designer.RehearseSandboxBanner.InnerTextAsync();
        Assert.That(bannerText.ToLowerInvariant(),
            Does.Contain("sandbox").And.Contain("nothing"),
            $"The sandbox banner should explain nothing is published; got '{bannerText}'.");

        // Before any rehearsal is started the "Start full rehearsal" button is shown (not the stepper).
        Assert.That(await Designer.RehearseFullStart.IsVisibleAsync(), Is.True,
            "Full-rehearsal mode should offer the 'Start full rehearsal' button before a run begins.");
    }

    [Test]
    [Retry(2)]
    public async Task Rehearse_ModeToggle_RoundTripsBetweenDryRunAndFull()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "rehearse");

        // Default → dry-run stepper visible.
        Assert.That(await Designer.RehearsalStepper.IsVisibleAsync(), Is.True,
            "Dry-run stepper should be visible by default.");

        // → Full: banner + start button, no stepper.
        await Designer.RehearseModeFull.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);
        Assert.That(await Designer.RehearseSandboxBanner.IsVisibleAsync(), Is.True,
            "Full mode should show the sandbox banner.");

        // → Back to dry-run: stepper returns, banner gone.
        await Designer.RehearseModeDryRun.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var stepperVisible = await Designer.RehearsalStepper.IsVisibleAsync();
        var bannerCount = await Designer.RehearseSandboxBanner.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stepperVisible, Is.True,
                "Switching back to dry-run should restore the stepper.");
            Assert.That(bannerCount, Is.EqualTo(0),
                "The sandbox banner should be gone after switching back to dry-run.");
        });
    }

    [Test]
    [Retry(2)]
    public async Task Rehearse_Route_StaysHealthy()
    {
        // Navigate cleanly and exercise the mode toggle. The base TearDown asserts no console errors,
        // no network 5xx, and MudBlazor CSS health for this authenticated navigation.
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "rehearse");
        await Designer.RehearseModeFull.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        Assert.Pass("Rehearse stage navigated cleanly; console/network/CSS health asserted in TearDown.");
    }
}
