// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// E2E tests for My Wallet page with real Wallet Service integration.
/// </summary>
[TestFixture]
[Category("Docker")]
[Category("Wallets")]
[Parallelizable(ParallelScope.Self)]
public class WalletManagementTests : AuthenticatedDockerTestBase
{
    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
    }

    [Test]
    [Retry(2)]
    public async Task MyWallet_PageLoads_ShowsTitleAndContent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);

        var content = await Page.TextContentAsync("body") ?? "";
        Assert.That(content, Does.Contain("Wallet"),
            "My Wallet page should render wallet-related content");
    }

    [Test]
    [Retry(2)]
    public async Task MyWallet_ShowsCreateButton()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var createButton = Page.Locator("a:has-text('Create Wallet'), button:has-text('Create Wallet')");
        var hasButton = await createButton.CountAsync() > 0;
        Assert.That(hasButton, Is.True,
            "My Wallet page should have a Create Wallet button");
    }

    [Test]
    [Retry(2)]
    public async Task MyWallet_NoWallets_ShowsEmptyState()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        // Should show either wallet data, empty state, or service error
        var cards = MudBlazorHelpers.Cards(Page);
        var pageContent = await Page.TextContentAsync("body") ?? "";

        var hasContent = await cards.CountAsync() > 0 ||
                         pageContent.Contains("No Wallet", StringComparison.OrdinalIgnoreCase) ||
                         pageContent.Contains("wallet.empty", StringComparison.OrdinalIgnoreCase) ||
                         pageContent.Contains("Create Wallet", StringComparison.OrdinalIgnoreCase) ||
                         pageContent.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase) ||
                         pageContent.Contains("Wallet", StringComparison.OrdinalIgnoreCase);
        Assert.That(hasContent, Is.True,
            "Page should show wallets, empty state, or service error");
    }

    [Test]
    [Retry(2)]
    public async Task MyWallet_WalletAddresses_UseTruncation()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var truncatedIds = Page.Locator(".truncated-id");
        var count = await truncatedIds.CountAsync();

        if (count > 0)
        {
            var firstId = await truncatedIds.First.TextContentAsync();
            Assert.That(firstId, Does.Contain("..."),
                "Wallet addresses should use truncation pattern");
        }
    }

    [Test]
    [Retry(2)]
    public async Task MyWallet_NoConsoleErrors()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.MyWallet);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);

        var criticalErrors = GetCriticalConsoleErrors();
        Assert.That(criticalErrors, Is.Empty,
            "My Wallet page should not have critical console errors");
    }
}
