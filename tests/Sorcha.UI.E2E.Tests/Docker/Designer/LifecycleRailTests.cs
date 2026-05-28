// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.Designer;

/// <summary>
/// E2E tests for the Feature 142 rail-driven designer shell (T012). Covers the always-visible
/// lifecycle rail, the governed Go-live lock, stage deep-linking via <c>?stage=</c>, the first-run
/// guided overlay, and console/network/CSS health on the designer route.
/// </summary>
/// <remarks>
/// Real-UI assertions (rail renders, lock present, stage nav, overlay) need no JS hooks and must
/// pass against the Release Docker image. The Go-live UNLOCK path drives the shell test seam
/// (<c>window.sorcha.designer.shellRef</c>) which is only present on DEBUG / E2E_TEST_HOOKS builds;
/// that test probes the seam and marks itself inconclusive when absent — unlock logic is covered
/// for real by the bUnit suite (T013).
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Designer")]
[Category("Lifecycle")]
[Category("Authenticated")]
public class LifecycleRailTests : DesignerSuiteBase
{
    [Test]
    [Retry(2)]
    public async Task LifecycleRail_DesignerLoads_RendersAllFourStages()
    {
        await OpenDesignerAsync();

        Assert.That(await Designer.Rail.CountAsync(), Is.GreaterThan(0),
            "The lifecycle rail should render on the designer route.");

        var describeCount = await Designer.RailDescribe.CountAsync();
        var understandCount = await Designer.RailUnderstand.CountAsync();
        var rehearseCount = await Designer.RailRehearse.CountAsync();
        var goLiveCount = await Designer.RailGoLive.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(describeCount, Is.GreaterThan(0), "rail-stage-describe present.");
            Assert.That(understandCount, Is.GreaterThan(0), "rail-stage-understand present.");
            Assert.That(rehearseCount, Is.GreaterThan(0), "rail-stage-rehearse present.");
            Assert.That(goLiveCount, Is.GreaterThan(0), "rail-stage-golive present.");
        });

        // With no blueprint loaded the workspace defaults to the Describe stage canvas.
        Assert.That(await Designer.StageDescribe.IsVisibleAsync(), Is.True,
            "The Describe stage canvas should be visible by default.");
    }

    [Test]
    [Retry(2)]
    public async Task LifecycleRail_GoLive_IsLockedWithPlainLanguageReason()
    {
        await OpenDesignerAsync();

        Assert.That(await Designer.GoLiveLock.CountAsync(), Is.GreaterThan(0),
            "Go live should start locked (no rehearsal recorded), so rail-golive-lock is present.");

        // Hover the locked Go-live stage to surface the explanatory MudTooltip. The stage button is
        // disabled and wrapped by MudTooltip's own root, which Playwright's strict actionability check
        // treats as a pointer-event interceptor — Force dispatches the hover so the tooltip shows.
        await Designer.RailGoLive.HoverAsync(new LocatorHoverOptions { Force = true });
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        // The MudTooltip copy explains the gate in plain language. Match on the live copy from
        // LifecycleRail.razor: "Rehearse a successful end-to-end run before publishing to a live register."
        var tooltip = Page.Locator(".mud-tooltip:has-text('Rehearse')").First;
        Assert.That(await tooltip.CountAsync(), Is.GreaterThan(0),
            "Hovering the locked Go-live stage should reveal a tooltip mentioning rehearsing before publishing.");

        var text = await tooltip.InnerTextAsync();
        Assert.That(text.ToLowerInvariant(), Does.Contain("rehearse").And.Contain("publish"),
            $"Go-live lock reason should mention rehearse and publish; got '{text}'.");
    }

    [Test]
    [Retry(2)]
    public async Task LifecycleRail_StageDeepLink_ShowsRequestedStageCanvas()
    {
        // ?stage=understand → Understand canvas. (Describe is always available; Understand is reachable
        // even without a blueprint via the deep-link parser, which sets the stage on initial load.)
        await Designer.NavigateToStageAsync("understand");
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(await Designer.StageUnderstand.IsVisibleAsync(), Is.True,
            "?stage=understand should render the Understand stage canvas.");

        // ?stage=describe → Describe canvas.
        await Designer.NavigateToStageAsync("describe");
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(await Designer.StageDescribe.IsVisibleAsync(), Is.True,
            "?stage=describe should render the Describe stage canvas.");
    }

    [Test]
    [Retry(2)]
    public async Task LifecycleRail_FirstRunOverlay_ShowsThenDismisses()
    {
        // A fresh browser context has no railTourDismissed flag, so the overlay should appear.
        // Open WITHOUT the harness auto-dismiss so we can assert the overlay then dismiss it ourselves.
        await OpenDesignerAsync(dismissFirstRunOverlay: false);

        Assert.That(await Designer.FirstRunOverlay.IsVisibleAsync(), Is.True,
            "The first-run guided overlay should appear on a fresh context.");

        await Designer.FirstRunDismiss.ClickAsync();
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        Assert.That(await Designer.FirstRunOverlay.IsVisibleAsync(), Is.False,
            "Dismissing the overlay should hide it.");
    }

    [Test]
    [Retry(2)]
    public async Task LifecycleRail_DesignerRoute_StaysHealthy()
    {
        // Navigate cleanly and exercise stage deep-links. The base TearDown asserts no console errors,
        // no network 5xx, and MudBlazor CSS health for this authenticated navigation.
        await OpenDesignerAsync();
        await Designer.NavigateToStageAsync("understand");
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.Pass("Designer route navigated cleanly; console/network/CSS health asserted in TearDown.");
    }

    [Test]
    [Retry(2)]
    public async Task LifecycleRail_GoLiveUnlock_ViaRehearsalSeam()
    {
        await OpenDesignerAsync();

        if (!await Designer.ShellSeamReadyAsync())
        {
            Assert.Inconclusive(
                "shell test seam unavailable — Release build without E2E_TEST_HOOKS; unlock logic covered by bUnit T013");
            return;
        }

        // Sanity: locked before the rehearsal is recorded.
        Assert.That(await Designer.GoLiveLock.CountAsync(), Is.GreaterThan(0),
            "Go live should be locked before a rehearsal passes.");

        await Designer.InvokeShellSeamAsync("TestInject_MarkRehearsalPassed");
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        Assert.That(await Designer.GoLiveLock.CountAsync(), Is.EqualTo(0),
            "Recording a passing rehearsal should clear the Go-live lock indicator.");
    }
}
