// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// E2E tests for the Feature 142 Go live stage (T033). With a smoke blueprint loaded at
/// <c>?stage=golive</c>, covers the Go-live panel surface — the target-register dropdown, the
/// permanence/versioning review summary, the rehearsal-gated (disabled) publish button, the
/// register system-info detail card / empty-state, and route health.
/// </summary>
/// <remarks>
/// Publish is gated on a passed full rehearsal AND a publishable register selection. A freshly
/// created blueprint has neither, so Publish is asserted disabled. The rehearsal-pass UNLOCK seam
/// (<c>window.sorcha.designer.shellRef.TestInject_MarkRehearsalPassed</c>) is DEBUG/E2E-only and
/// absent on the Release Docker image; that branch mirrors
/// <c>LifecycleRailTests.LifecycleRail_GoLiveUnlock_ViaRehearsalSeam</c> and marks itself
/// inconclusive when the seam is unavailable. Register details depend on the admin's actual
/// subscriptions, so the test handles both "registers selectable" and "no eligible registers"
/// without failing. A blueprint is provisioned via the REST API; provisioning failures are
/// inconclusive, not failures.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Designer")]
[Category("Lifecycle")]
[Category("GoLive")]
[Category("Authenticated")]
public class GoLiveTests : DesignerSuiteBase
{
    [Test]
    [Retry(2)]
    public async Task GoLive_Panel_RendersRegisterPickerOrEmptyState()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        Assert.That(await Designer.StageGoLive.IsVisibleAsync(), Is.True,
            "?stage=golive should render the Go live stage canvas.");
        Assert.That(await Designer.GoLivePanel.CountAsync(), Is.GreaterThan(0),
            "The Go-live panel should render for a loaded blueprint.");

        // Either the admin has eligible (subscribed, online, non-sandbox) registers — showing the
        // dropdown — or the helpful empty-state is shown. Both are valid; never fail on which one.
        var hasPicker = await Designer.GoLiveRegisterSelect.CountAsync() > 0;
        var hasEmptyState = await Designer.GoLiveNoEligibleRegisters.CountAsync() > 0;

        Assert.That(hasPicker || hasEmptyState, Is.True,
            "Go-live should render either the register dropdown or the 'no eligible registers' empty state.");

        if (hasEmptyState)
        {
            var emptyText = await Designer.GoLiveNoEligibleRegisters.InnerTextAsync();
            Assert.That(emptyText.ToLowerInvariant(), Does.Contain("register"),
                $"The empty-state should explain the lack of an eligible register; got '{emptyText}'.");
        }
    }

    [Test]
    [Retry(2)]
    public async Task GoLive_ReviewSummary_RendersWhenRegistersAvailable()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        // The review summary (permanence + versioning notice + version) only renders when there is at
        // least one eligible register to publish to. If the admin has none, the panel shows the
        // empty-state instead — that is a valid environment shape, so mark inconclusive.
        if (await Designer.GoLiveRegisterSelect.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "No eligible (subscribed, online, non-sandbox) register for the admin; review summary not rendered. "
                + "Empty-state path is covered by GoLive_Panel_RendersRegisterPickerOrEmptyState.");
            return;
        }

        Assert.That(await Designer.GoLiveReview.IsVisibleAsync(), Is.True,
            "The review summary should render when a register picker is available.");
        Assert.That(await Designer.GoLiveVersion.CountAsync(), Is.GreaterThan(0),
            "The review summary should show the blueprint version (golive-version).");

        var reviewText = await Designer.GoLiveReview.InnerTextAsync();
        Assert.That(reviewText.ToLowerInvariant(), Does.Contain("version"),
            $"The review summary should mention versioning/permanence; got '{reviewText}'.");
    }

    [Test]
    [Retry(2)]
    public async Task GoLive_Publish_IsGatedUntilRehearsalPasses()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        // The publish button only renders when there is a register picker (i.e. eligible registers).
        if (await Designer.GoLivePublishButton.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "No eligible register for the admin, so the publish button is not rendered; "
                + "the rehearsal gate is enforced regardless of register availability.");
            return;
        }

        // A freshly created blueprint has NOT passed a full rehearsal, so Publish must be disabled
        // and the plain-language locked hint shown.
        Assert.That(await Designer.GoLivePublishButton.IsDisabledAsync(), Is.True,
            "Publish should be disabled until a full rehearsal has passed for a freshly created blueprint.");

        Assert.That(await Designer.GoLiveLockedHint.CountAsync(), Is.GreaterThan(0),
            "The plain-language locked hint (rehearse before publishing) should be shown while gated.");

        var hintText = await Designer.GoLiveLockedHint.InnerTextAsync();
        Assert.That(hintText.ToLowerInvariant(), Does.Contain("rehearse").And.Contain("publish"),
            $"The locked hint should mention rehearsing before publishing; got '{hintText}'.");
    }

    [Test]
    [Retry(2)]
    public async Task GoLive_PublishUnlocks_ViaRehearsalSeam()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        if (!await Designer.ShellSeamReadyAsync())
        {
            Assert.Inconclusive(
                "shell test seam unavailable — Release build without E2E_TEST_HOOKS; unlock logic covered by bUnit.");
            return;
        }

        if (await Designer.GoLivePublishButton.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "No eligible register for the admin, so the publish button is not rendered; "
                + "cannot assert the seam-driven unlock of the publish affordance.");
            return;
        }

        // Sanity: gated before the rehearsal is recorded.
        Assert.That(await Designer.GoLivePublishButton.IsDisabledAsync(), Is.True,
            "Publish should be disabled before a rehearsal passes.");

        await Designer.InvokeShellSeamAsync("TestInject_MarkRehearsalPassed");
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The locked hint should clear once the rehearsal gate is satisfied. (The button may still be
        // disabled until a register is also selected — assert the gate hint, not full enablement.)
        Assert.That(await Designer.GoLiveLockedHint.CountAsync(), Is.EqualTo(0),
            "Recording a passing rehearsal should clear the Go-live locked hint.");
    }

    [Test]
    [Retry(2)]
    public async Task GoLive_SystemInfoCard_RendersOnRegisterSelect()
    {
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        if (await Designer.GoLiveRegisterSelect.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "No eligible (subscribed, online, non-sandbox) register for the admin to select; "
                + "system-info card needs a selectable register. Sandbox registers are correctly excluded.");
            return;
        }

        // Open the MudSelect and pick the first option to trigger the system-info read.
        await Designer.GoLiveRegisterSelect.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var options = Page.Locator(".mud-popover .mud-list-item");
        if (await options.CountAsync() == 0)
        {
            Assert.Inconclusive("Register dropdown opened but exposed no selectable options.");
            return;
        }

        await options.First.ClickAsync();
        // The system-info read is a backend round-trip; give it time then assert the card appears.
        await Designer.GoLiveSystemInfoCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = TestConstants.ElementTimeout,
        });

        Assert.That(await Designer.GoLiveSystemInfoCard.IsVisibleAsync(), Is.True,
            "Selecting a register should render its system-info detail card (D6 / FR-026).");
    }

    [Test]
    [Retry(2)]
    public async Task GoLive_Route_StaysHealthy()
    {
        // Navigate cleanly. The base TearDown asserts no console errors, no network 5xx, and
        // MudBlazor CSS health for this authenticated navigation.
        var blueprintId = await CreateSmokeBlueprintAsync();
        await OpenDesignerStageAsync(blueprintId, "golive");

        Assert.That(await Designer.GoLivePanel.CountAsync(), Is.GreaterThan(0),
            "Go live panel should render.");

        Assert.Pass("Go live stage navigated cleanly; console/network/CSS health asserted in TearDown.");
    }
}
