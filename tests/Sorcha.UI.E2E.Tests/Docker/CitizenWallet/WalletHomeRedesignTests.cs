// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Feature 141 — E2E coverage for the "Bolder" wallet Home reskin. Runs
/// against the empty-wallet (unauthenticated) state the existing
/// <see cref="CitizenWalletNavigationTests"/> harness already exercises:
/// the gradient hero, the empty ghost card stack, the big Present/Verify
/// action pair, and the floating tab bar. Populated/dark-mode/authenticated
/// flows need an enrolled-citizen fixture (issue #700) and are covered at the
/// component level by the bunit WalletChromeComponentTests.
/// </summary>
public class WalletHomeRedesignTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    [Test]
    [Retry(2)]
    public async Task EmptyHome_RendersHero_GhostStack_AndActionPair()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Assertions.Expect(Wallet.Hero).ToBeVisibleAsync();
        await Assertions.Expect(Wallet.HeroEyebrow).ToHaveTextAsync("WELCOME");
        await Assertions.Expect(Wallet.HeroHeadline).ToHaveTextAsync("Your wallet is empty");
        await Assertions.Expect(Wallet.GhostCardStack).ToBeVisibleAsync();
        await Assertions.Expect(Wallet.BigPresent).ToBeVisibleAsync();
        await Assertions.Expect(Wallet.BigVerify).ToBeVisibleAsync();
    }

    [Test]
    [Retry(2)]
    public async Task EmptyHome_PresentAction_IsDisabled()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // With no credentials the primary Present action must not activate (FR-006).
        await Assertions.Expect(Wallet.BigPresent).ToBeDisabledAsync();
    }

    [Test]
    [Retry(2)]
    public async Task EmptyHome_VerifyAction_NavigatesToVerify()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Wallet.BigVerify.ClickAsync();
        await Page.WaitForURLAsync("**/wallet/verify*",
            new() { Timeout = 5000, WaitUntil = WaitUntilState.Load });

        Assert.That(new Uri(Page.Url).AbsolutePath, Does.StartWith("/wallet/verify"));
    }

    [Test]
    [Retry(2)]
    public async Task GhostTopCard_NavigatesToEnrol()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // The ghost stack's top card carries the enrol-device-button test id.
        await Wallet.EnrolDeviceButton.ClickAsync();
        await Page.WaitForURLAsync("**/wallet/enrol*",
            new() { Timeout = 5000, WaitUntil = WaitUntilState.Load });

        Assert.That(new Uri(Page.Url).AbsolutePath, Does.StartWith("/wallet/enrol"));
    }

    [Test]
    [Retry(2)]
    public async Task FloatingTabBar_IsVisible_WithHomeTabActive()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Assertions.Expect(Wallet.FloatingTabBar).ToBeVisibleAsync();

        // Home tab is the active pill on the wallet root.
        var home = Page.Locator("[data-testid='footer-nav-home']");
        await Assertions.Expect(home).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tab-pill--active"));
    }
}
