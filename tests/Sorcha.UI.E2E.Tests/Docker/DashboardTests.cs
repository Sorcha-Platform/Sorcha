// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for the Dashboard page against Docker.
/// These tests run authenticated (login handled by base class).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Dashboard")]
[Category("Authenticated")]
public class DashboardTests : AuthenticatedDockerTestBase
{
    private DashboardPage _dashboard = null!;
    private NavigationComponent _nav = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _dashboard = new DashboardPage(Page);
        _nav = new NavigationComponent(Page);
    }

    #region Smoke Tests

    [Test]
    [Retry(3)]
    public async Task Dashboard_LoadsSuccessfully()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.IsLoadedAsync(), Is.True,
            "Dashboard should render with welcome heading and container");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_ShowsWelcomeMessage()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var welcome = await _dashboard.GetWelcomeMessageAsync();
        Assert.That(welcome, Is.Not.Null,
            "Dashboard should display a welcome message (translated or raw key)");
    }

    [Test]
    public async Task Dashboard_HasCorrectPageTitle()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(Page).ToHaveTitleAsync(
            new System.Text.RegularExpressions.Regex("Dashboard|Sorcha"));
    }

    #endregion

    #region Stat Cards Tests

    [Test]
    [Retry(2)]
    public async Task Dashboard_ShowsStatCards()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var cardCount = await _dashboard.GetStatCardCountAsync();
        Assert.That(cardCount, Is.EqualTo(6),
            "Dashboard should show 6 stat cards (Blueprints, Wallets, Transactions, Peers, Registers, Organizations)");
    }

    [Test]
    public async Task Dashboard_ShowsBlueprintsStat()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.BlueprintsStat.CountAsync(), Is.GreaterThan(0),
            "Blueprints stat card should be visible");
    }

    [Test]
    public async Task Dashboard_ShowsWalletsStat()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.WalletsStat.CountAsync(), Is.GreaterThan(0),
            "Wallets stat card should be visible");
    }

    [Test]
    public async Task Dashboard_ShowsTransactionsStat()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.TransactionsStat.CountAsync(), Is.GreaterThan(0),
            "Transactions stat card should be visible");
    }

    #endregion

    #region Quick Actions Tests

    [Test]
    [Retry(2)]
    public async Task Dashboard_ShowsQuickActions()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.AreQuickActionsVisibleAsync(), Is.True,
            "Quick actions section should be visible");
    }

    [Test]
    public async Task Dashboard_CreateBlueprintLink_IsPresent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(_dashboard.CreateBlueprintButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ManageWalletsLink_IsPresent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(_dashboard.ManageWalletsButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ViewRegistersLink_IsPresent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(_dashboard.ViewRegistersButton).ToBeVisibleAsync();
    }

    #endregion

    #region Recent Activity Tests

    [Test]
    public async Task Dashboard_ShowsRecentActivitySection()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        await Expect(_dashboard.RecentActivity).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_RecentActivity_ShowsEmptyState()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        // On a fresh system, recent activity should show empty state
        Assert.That(await _dashboard.IsRecentActivityEmptyAsync(), Is.True,
            "Fresh system should show empty state for recent activity");
    }

    #endregion

    #region Responsive Design Tests

    [Test]
    public async Task Dashboard_Mobile_LoadsCorrectly()
    {
        await Page.SetViewportSizeAsync(375, 667);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.IsLoadedAsync(), Is.True,
            "Dashboard should render on mobile viewport");
    }

    [Test]
    public async Task Dashboard_Tablet_LoadsCorrectly()
    {
        await Page.SetViewportSizeAsync(768, 1024);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.IsLoadedAsync(), Is.True,
            "Dashboard should render on tablet viewport");
    }

    [Test]
    public async Task Dashboard_Desktop_LoadsCorrectly()
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        Assert.That(await _dashboard.IsLoadedAsync(), Is.True,
            "Dashboard should render on desktop viewport");
    }

    #endregion
}
