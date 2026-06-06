// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Round-2 PWA polish regression guard (PR #970). The Settings page lives inside
/// <c>.wallet-content</c>, whose bottom reserve was bumped 92px → 116px so the
/// last block clears the floating tab bar plus its drop-shadow. This test pins
/// that contract: at a short phone viewport, scrolled to the very bottom, the
/// last Settings block must sit fully above the floating bar.
/// </summary>
/// <remarks>
/// Settings is <c>[Authorize]</c>-gated. In environments without the citizen
/// enrolment fixture (issue #700) the page redirects to <c>/signin</c> and the
/// Settings content never renders — the test then ignores itself rather than
/// failing, the same pattern <c>CitizenWalletNavigationTests</c> uses for
/// auth-gated surfaces. It becomes a live guard once the #700 fixture lands.
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
public class PwaSettingsTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    [Test]
    [Retry(2)]
    public async Task Settings_LastBlock_ClearsFloatingTabBar_AtPhoneHeight()
    {
        // A short viewport guarantees the page scrolls, so the last block can
        // collide with the bottom-fixed floating bar if the reserve is too small.
        await Page.SetViewportSizeAsync(390, 680);
        await NavigateToWalletAndWaitForBlazorAsync("/settings");

        // The Replay-tour card is the last block on the page.
        var lastBlock = Page.Locator("[data-testid='settings-replay-tour-card']");
        if (await lastBlock.CountAsync() == 0)
        {
            Assert.Ignore(
                "Settings content not rendered — the page is [Authorize]-gated and " +
                "redirected to /signin. Needs the issue #700 enrolment fixture.");
        }

        // Scroll to the absolute bottom — the worst case for bottom-bar overlap.
        await Page.EvaluateAsync(
            "() => window.scrollTo(0, document.documentElement.scrollHeight)");

        var cardBox = await lastBlock.BoundingBoxAsync();
        var bar = Wallet.FloatingTabBar;
        await Assertions.Expect(bar).ToBeVisibleAsync(new() { Timeout = 3000 });
        var barBox = await bar.BoundingBoxAsync();

        Assert.That(cardBox, Is.Not.Null, "Last Settings block has no layout box.");
        Assert.That(barBox, Is.Not.Null, "Floating tab bar has no layout box.");

        // The last block's bottom edge must not dip below the bar's top edge
        // (1px rounding tolerance). If it does, the .wallet-content bottom
        // reserve is too small and the block peeks under the bar.
        var cardBottom = cardBox!.Y + cardBox.Height;
        Assert.That(cardBottom, Is.LessThanOrEqualTo(barBox!.Y + 1),
            $"Last Settings block (bottom {cardBottom}px) overlaps the floating " +
            $"tab bar (top {barBox.Y}px) — .wallet-content bottom reserve too small.");
    }
}
