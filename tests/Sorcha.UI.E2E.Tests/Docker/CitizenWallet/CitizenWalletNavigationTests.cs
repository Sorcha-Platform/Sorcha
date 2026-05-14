// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Click-every-nav-button coverage for the Citizen Wallet PWA. Defends
/// against a regression of PR #698, where every <c>NavigateTo(\"/x\")</c>
/// in the PWA resolved to the origin (<c>/x</c>) instead of the
/// <c>/wallet/x</c> base href — silently 404-ing every navigation button.
/// The pre-existing <see cref="CitizenWalletPushTests"/> suite covered
/// Home page render + demo-mint plumbing but never clicked a nav button,
/// which is how the bug shipped. Tracking gap: issue #700.
/// </summary>
/// <remarks>
/// <para>
/// Every test in this fixture navigates the wallet shell from <c>/wallet/</c>,
/// clicks one navigation-triggering element, and asserts the resulting URL
/// matches the expected <c>/wallet/&lt;page&gt;</c> path. The exact element
/// locators live on <see cref="CitizenWalletPage"/> — keeping the visible-
/// surface→URL contract in one structural place.
/// </para>
/// <para>
/// All assertions are auth-free: every probe runs against the PWA's
/// unauthenticated state. Auth-gated nav (Enrol Done → Home, Settings sign-
/// in flow, CredentialDetail surfaces) is deferred to the Phase 3 of
/// issue #700 once an enrolment fixture is added.
/// </para>
/// </remarks>
public class CitizenWalletNavigationTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    /// <summary>
    /// Expected URL path after each nav click. All paths must live under
    /// <c>/wallet/</c> — anything resolving to an origin-relative path
    /// (e.g. <c>/enrol</c> instead of <c>/wallet/enrol</c>) is the exact
    /// regression PR #698 fixed.
    /// </summary>
    private const string WalletBase = "/wallet/";

    private async Task AssertNavigatedToAsync(string expectedSuffix)
    {
        // Playwright's WaitForURLAsync uses glob by default. Anchor on the
        // wallet base + the expected suffix; trailing query-strings are
        // accepted. Five-second window covers Blazor's client-side route
        // resolution + StateHasChanged round-trip on slow CI.
        await Page.WaitForURLAsync(
            $"**{WalletBase}{expectedSuffix}*",
            new() { Timeout = 5000, WaitUntil = WaitUntilState.Load });

        var actual = new Uri(Page.Url).AbsolutePath;
        Assert.That(actual, Does.StartWith($"{WalletBase}{expectedSuffix}"),
            $"After clicking the nav element, URL path should start with " +
            $"'{WalletBase}{expectedSuffix}' but was '{actual}'. " +
            $"Leading-slash NavigateTo regression?");
    }

    private static IEnumerable<TestCaseData> FooterNavCases
    {
        get
        {
            yield return new TestCaseData("footer-nav-devices", "devices").SetName("Footer Devices → /wallet/devices");
            yield return new TestCaseData("footer-nav-activity", "activity").SetName("Footer Activity → /wallet/activity");
            yield return new TestCaseData("footer-nav-settings", "settings").SetName("Footer Settings → /wallet/settings");
        }
    }

    [Test]
    [TestCaseSource(nameof(FooterNavCases))]
    [Retry(2)]
    public async Task FooterNav_ClickRoutesToExpectedPage(string testId, string expectedSuffix)
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Page.Locator($"[data-testid='{testId}']").ClickAsync();
        await AssertNavigatedToAsync(expectedSuffix);
    }

    [Test]
    [Retry(2)]
    public async Task FooterNav_Home_ClickRoutesBackToWalletRoot()
    {
        // Reach a non-Home page first so we can prove Home actually navigates
        // back. The Devices page is the cheapest reachable destination.
        await NavigateToWalletAndWaitForBlazorAsync("/devices");
        await Wallet.FooterNavHome.ClickAsync();

        await Page.WaitForURLAsync($"**{WalletBase}*",
            new() { Timeout = 5000, WaitUntil = WaitUntilState.Load });

        var actual = new Uri(Page.Url).AbsolutePath;
        // The Home button uses NavigateTo("") so the resulting path is the
        // base /wallet/ exactly (no trailing segment).
        Assert.That(actual, Is.EqualTo(WalletBase).Or.EqualTo("/wallet"),
            $"Home button should land at the wallet base, was '{actual}'. " +
            $"NavigateTo(\"/\") regression?");
    }

    [Test]
    [Retry(2)]
    public async Task EnrolDeviceButton_ClickRoutesToEnrolPage()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // Empty-state Enrol button is only visible when no credentials are
        // cached. Pre-condition matches the typical first-run citizen
        // experience exercised in the F124 demo.
        await Wallet.EnrolDeviceButton.ClickAsync();
        await AssertNavigatedToAsync("enrol");
    }

    [Test]
    [Retry(2)]
    public async Task TopBarPresentButton_ClickRoutesToPresentPage()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Wallet.TopBarPresentButton.ClickAsync();
        await AssertNavigatedToAsync("present");
    }

    [Test]
    [Retry(2)]
    public async Task EnrolPage_OpenSettingsLink_ClickRoutesToSettingsPage()
    {
        // Enrol page in its signed-out Welcome step renders an "Open Settings"
        // MudLink. Asserting it lands at /wallet/settings exercises the
        // Href="..." attribute path that Blazor's MudLink renders — a parallel
        // code path to NavigateTo that PR #698 also fixed.
        await NavigateToWalletAndWaitForBlazorAsync("/enrol");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The link only renders in the signed-out branch. If a prior test
        // bled into shared storage (unlikely in citizen-wallet which uses
        // IndexedDB-not-cookies), fall back gracefully.
        if (await Wallet.EnrolOpenSettingsLink.CountAsync() == 0)
        {
            Assert.Inconclusive(
                "Enrol page's Open Settings link is not visible — Welcome step " +
                "probably entered the signed-in branch. Run on a clean IndexedDB.");
            return;
        }

        await Wallet.EnrolOpenSettingsLink.ClickAsync();
        await AssertNavigatedToAsync("settings");
    }
}
