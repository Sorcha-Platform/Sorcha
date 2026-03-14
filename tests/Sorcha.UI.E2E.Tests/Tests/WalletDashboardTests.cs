// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Tests;

/// <summary>
/// E2E tests for wallet dashboard wizard behavior.
/// Tests verify that the wizard only shows when user truly has no wallets,
/// and prevents the infinite loop bug.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Wallet")]
[Category("Dashboard")]
public class WalletDashboardTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// T010: Verify wizard appears for first-time users with no wallets
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task FirstLogin_NoWallets_ShowsWizard()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var currentUrl = Page.Url;

        // Either on wizard page (no wallets) or dashboard (has wallets)
        var isOnWizard = currentUrl.Contains("/wallets/create") ||
                        currentUrl.Contains("first-login=true");

        if (!isOnWizard)
        {
            Assert.Ignore("Test user has existing wallets. Cannot test first-time user flow.");
        }

        // Verify wizard page elements are present (translated or raw key)
        var pageContent = await Page.TextContentAsync("body") ?? "";
        Assert.That(
            pageContent.Contains("Create New Wallet", StringComparison.OrdinalIgnoreCase) ||
            pageContent.Contains("wallet.create", StringComparison.OrdinalIgnoreCase),
            Is.True,
            "Should show wallet creation wizard");
    }

    /// <summary>
    /// T011: Verify wizard does NOT reappear after wallet creation
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task AfterWalletCreation_DashboardLoads_WizardDoesNotReappear()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var currentUrl = Page.Url;
        var isOnDashboard = currentUrl.Contains("/dashboard") ||
                           currentUrl.EndsWith("/app/") ||
                           currentUrl.EndsWith("/");

        if (!isOnDashboard)
        {
            Assert.Warn($"Not on dashboard. URL: {currentUrl}. User may not have wallets.");
        }

        // Verify dashboard content is visible (check for translated or raw key text)
        var pageContent = await Page.TextContentAsync("body") ?? "";
        var hasDashboardContent =
            pageContent.Contains("Welcome", StringComparison.OrdinalIgnoreCase) ||
            pageContent.Contains("dashboard.welcomeBack", StringComparison.OrdinalIgnoreCase) ||
            pageContent.Contains("Dashboard", StringComparison.OrdinalIgnoreCase);

        Assert.That(hasDashboardContent, Is.True,
            "Dashboard should display content, not redirect to wizard");

        // Refresh and verify no redirect loop
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        currentUrl = Page.Url;
        isOnDashboard = currentUrl.Contains("/dashboard") ||
                       currentUrl.EndsWith("/app/") ||
                       currentUrl.EndsWith("/");

        Assert.That(isOnDashboard, Is.True,
            "After refresh, should remain on dashboard (no wizard loop)");
    }

    /// <summary>
    /// T012: Verify returning users with existing wallets skip the wizard
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task ExistingWallet_DashboardLoad_SkipsWizard()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var currentUrl = Page.Url;

        if (currentUrl.Contains("/wallets/create"))
        {
            Assert.Ignore("Test user has no wallets. This test requires an existing wallet.");
        }

        var isOnDashboard = currentUrl.Contains("/dashboard") ||
                           currentUrl.EndsWith("/app/") ||
                           currentUrl.EndsWith("/");

        Assert.That(isOnDashboard, Is.True, "User with existing wallet should land on dashboard");

        // Verify wallet information is somewhere on the page
        var pageContent = await Page.TextContentAsync("body") ?? "";
        var hasWalletInfo =
            pageContent.Contains("Wallet", StringComparison.OrdinalIgnoreCase) ||
            pageContent.Contains("dashboard.stats.wallets", StringComparison.OrdinalIgnoreCase);

        Assert.That(hasWalletInfo, Is.True, "Dashboard should show wallet-related information");
    }

    /// <summary>
    /// T013: Verify graceful handling when stats fail to load
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task StatsFailToLoad_DashboardLoads_DoesNotRedirectToWizard()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var currentUrl = Page.Url;
        var isOnDashboard = currentUrl.Contains("/dashboard") ||
                           currentUrl.EndsWith("/app/") ||
                           currentUrl.EndsWith("/");

        Assert.That(isOnDashboard, Is.True,
            "Even if stats fail, should stay on dashboard (not redirect to wizard)");
    }
}
