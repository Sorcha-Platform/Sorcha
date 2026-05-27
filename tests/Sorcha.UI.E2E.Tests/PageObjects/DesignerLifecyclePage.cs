// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Feature 142 rail-driven designer workspace at
/// <c>/app/designer/blueprint</c>. Selectors use the stable <c>data-testid</c>
/// attributes carried by the lifecycle-rail / journey / stage components.
/// </summary>
/// <remarks>
/// Scaffold introduced by T001/T002; locators target the test-ids the User Story
/// components (LifecycleRail T014, JourneyView T016, stage canvases) will expose.
/// Story suites under <c>Docker/Designer/</c> add the actual assertions.
/// </remarks>
public class DesignerLifecyclePage
{
    private readonly IPage _page;

    public DesignerLifecyclePage(IPage page) => _page = page;

    public string BaseUrl => $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.DesignerBlueprint}";

    // --- Lifecycle rail ---------------------------------------------------
    public ILocator Rail => _page.Locator("[data-testid='lifecycle-rail']");
    public ILocator RailDescribe => _page.Locator("[data-testid='rail-stage-describe']");
    public ILocator RailUnderstand => _page.Locator("[data-testid='rail-stage-understand']");
    public ILocator RailRehearse => _page.Locator("[data-testid='rail-stage-rehearse']");
    public ILocator RailGoLive => _page.Locator("[data-testid='rail-stage-golive']");

    /// <summary>The lock indicator on the Go live rail stage (present while locked).</summary>
    public ILocator GoLiveLock => _page.Locator("[data-testid='rail-golive-lock']");

    // --- Stage canvases (root markers established in T001) -----------------
    public ILocator StageDescribe => _page.Locator("[data-testid='stage-describe']");
    public ILocator StageUnderstand => _page.Locator("[data-testid='stage-understand']");
    public ILocator StageRehearse => _page.Locator("[data-testid='stage-rehearse']");
    public ILocator StageGoLive => _page.Locator("[data-testid='stage-golive']");

    // --- Understand / journey --------------------------------------------
    public ILocator JourneyView => _page.Locator("[data-testid='journey-view']");
    public ILocator JourneySteps => _page.Locator("[data-testid^='journey-step-']");
    public ILocator MustProveBadges => _page.Locator("[data-testid='journey-badge-mustprove']");
    public ILocator IssuesBadges => _page.Locator("[data-testid='journey-badge-issues']");
    public ILocator TechnicalFlowToggle => _page.Locator("[data-testid='understand-technical-toggle']");

    public Task NavigateAsync(string? blueprintId = null)
    {
        var url = BaseUrl;
        if (!string.IsNullOrEmpty(blueprintId))
        {
            url += "/" + blueprintId;
        }
        return _page.GotoAsync(url);
    }
}
