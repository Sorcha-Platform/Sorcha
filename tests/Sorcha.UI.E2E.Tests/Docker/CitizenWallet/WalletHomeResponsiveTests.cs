// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Feature 141 — responsive + accessibility coverage for the reskinned wallet
/// Home: no horizontal overflow at phone and tablet widths (FR-022 / SC-004),
/// and descriptive accessible names on the action buttons, the ghost top
/// card, and the floating-bar tabs (FR-024).
/// </summary>
public class WalletHomeResponsiveTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    private static IEnumerable<TestCaseData> ViewportCases
    {
        get
        {
            yield return new TestCaseData(375, 740).SetName("Phone 375x740 — no horizontal overflow");
            yield return new TestCaseData(768, 1024).SetName("Tablet 768x1024 — no horizontal overflow");
        }
    }

    [Test]
    [TestCaseSource(nameof(ViewportCases))]
    [Retry(2)]
    public async Task Home_NoHorizontalOverflow(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // Allow a 1px rounding tolerance; anything more means a region bleeds
        // past the viewport (the floating bar, hero, or action grid).
        var overflow = await Page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - window.innerWidth");

        Assert.That(overflow, Is.LessThanOrEqualTo(1),
            $"Home overflows horizontally by {overflow}px at {width}x{height}.");
    }

    [Test]
    [Retry(2)]
    public async Task ActionsAndTabs_HaveAccessibleNames()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // Action buttons compose name from title + subtitle.
        Assert.That(await Wallet.BigPresent.GetAttributeAsync("aria-label"), Does.Contain("Present"));
        Assert.That(await Wallet.BigVerify.GetAttributeAsync("aria-label"), Does.Contain("Verify"));

        // Ghost top card (enrol tap-target).
        Assert.That(await Wallet.EnrolDeviceButton.GetAttributeAsync("aria-label"),
            Does.Contain("Add a credential"));

        // Every floating-bar tab exposes a name even when visually icon-only.
        foreach (var (id, name) in new[]
                 {
                     ("footer-nav-home", "Home"),
                     ("footer-nav-devices", "Devices"),
                     ("footer-nav-activity", "Activity"),
                     ("footer-nav-settings", "Settings"),
                 })
        {
            var label = await Page.Locator($"[data-testid='{id}']").GetAttributeAsync("aria-label");
            Assert.That(label, Is.EqualTo(name), $"Tab {id} should be named '{name}'.");
        }
    }
}
