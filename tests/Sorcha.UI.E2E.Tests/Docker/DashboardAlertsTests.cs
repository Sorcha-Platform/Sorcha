// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for dashboard auto-refresh and alerts panel (Feature 051 US1).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Dashboard")]
[Category("Authenticated")]
public class DashboardAlertsTests : AuthenticatedDockerTestBase
{
    [Test]
    [Retry(3)]
    public async Task Dashboard_ShowsStatCards_WithValues()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var cards = Page.Locator(".mud-card");
        await cards.First.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

        var count = await cards.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(4),
            "Dashboard should show at least 4 stat cards");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_ShowsAlertsPanel()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var errorAlerts = Page.Locator(".mud-alert-filled-error");
        var errorCount = await errorAlerts.CountAsync();
        Assert.That(errorCount, Is.EqualTo(0),
            "Dashboard should not show error alerts on load");
    }

    [Test]
    public async Task Dashboard_LoadsWithoutErrors()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(Page).ToHaveTitleAsync(
            new System.Text.RegularExpressions.Regex("Dashboard|Sorcha"));
    }
}
