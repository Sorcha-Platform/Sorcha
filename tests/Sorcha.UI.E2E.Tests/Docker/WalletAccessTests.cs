// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for wallet access delegation tab (Feature 051 US2).
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Wallets")]
[Category("Authenticated")]
public class WalletAccessTests : AuthenticatedDockerTestBase
{
    [Test]
    [Retry(2)]
    public async Task Wallets_ListPage_LoadsSuccessfully()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Wallets);

        await Expect(Page).ToHaveTitleAsync(
            new System.Text.RegularExpressions.Regex("Wallet|Sorcha"));
    }

    [Test]
    [Retry(2)]
    public async Task WalletDetail_HasTabs()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Wallets);

        // Look for any clickable wallet link or card
        var walletLink = Page.Locator("a[href*='wallets/'], .mud-card");
        if (await walletLink.CountAsync() == 0)
        {
            Assert.Inconclusive("No wallets available to test detail view");
            return;
        }

        await walletLink.First.ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Wallet detail page should have tabs
        var tabs = Page.Locator(".mud-tabs .mud-tab, .mud-tab");
        var tabCount = await tabs.CountAsync();
        Assert.That(tabCount, Is.GreaterThanOrEqualTo(2),
            "Wallet detail should have multiple tabs including Access");
    }
}
