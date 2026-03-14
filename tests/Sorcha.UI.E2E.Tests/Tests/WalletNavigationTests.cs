// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Tests;

/// <summary>
/// E2E tests for wallet navigation URL correctness.
/// Tests verify that wallet detail navigation includes proper /app/ base href.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Wallet")]
[Category("Navigation")]
public class WalletNavigationTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// T024: Verify wallet navigation URLs include /app/ prefix
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task MyWallet_ClickWallet_NavigatesToCorrectUrl()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);

        // Find wallet cards
        var walletCards = Page.Locator(".mud-card");
        if (await walletCards.CountAsync() == 0)
        {
            Assert.Ignore("No wallets found. Test requires at least one wallet.");
            return;
        }

        await walletCards.First.ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        Assert.That(Page.Url, Does.Contain("/app/wallets/"),
            $"URL should include '/app/wallets/' prefix. Actual: {Page.Url}");
    }

    /// <summary>
    /// T025: Verify wallet detail page loads successfully after navigation
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task MyWallet_ClickWallet_PageLoadsSuccessfully()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);

        var walletCards = Page.Locator(".mud-card");
        if (await walletCards.CountAsync() == 0)
        {
            Assert.Ignore("No wallets found. Test requires at least one wallet.");
            return;
        }

        await walletCards.First.ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        Assert.That(Page.Url, Does.Contain("/wallets/"), "Should be on wallet detail page");

        // Verify no 404
        var pageContent = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageContent, Does.Not.Contain("Page Not Found"),
            "Wallet detail page should load successfully");
    }

    /// <summary>
    /// T026: Verify bookmarked wallet URLs work correctly
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task WalletDetailUrl_DirectAccess_PageLoads()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);

        var walletCards = Page.Locator(".mud-card");
        if (await walletCards.CountAsync() == 0)
        {
            Assert.Ignore("No wallets found. Test requires at least one wallet.");
            return;
        }

        // Click to get wallet detail URL
        await walletCards.First.ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        var walletDetailUrl = Page.Url;

        // Navigate away, then back (simulate bookmark)
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Dashboard);
        await Page.GotoAsync(walletDetailUrl);
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        Assert.That(Page.Url, Does.Contain("/wallets/"),
            "Bookmarked URL should work when accessed directly");

        var pageContent = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageContent, Does.Not.Contain("Page Not Found"),
            "Bookmarked wallet URL should not show 404");
    }
}
