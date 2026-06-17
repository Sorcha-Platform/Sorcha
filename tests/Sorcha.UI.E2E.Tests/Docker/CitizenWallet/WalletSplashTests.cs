// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Guards the Wallet PWA pre-boot loading splash: the markup + assets are
/// served in index.html, and the splash is removed once Blazor renders the
/// app into #app (proving the splash wiring did not break boot).
/// </summary>
public class WalletSplashTests : AuthenticatedCitizenWalletTestBase
{
    private static string WalletUrl(string suffix = "") =>
        $"{TestConstants.UiWebUrl}{TestConstants.CitizenWalletBase}{suffix}";

    [Test]
    public async Task IndexHtml_ContainsSplashMarkupAndStylesheet()
    {
        using var http = new HttpClient();
        var html = await http.GetStringAsync(WalletUrl());

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("id=\"sorcha-splash\""), "splash root present");
            Assert.That(html, Does.Contain("sorcha-splash-canvas"), "canvas present");
            Assert.That(html, Does.Contain("sorcha-splash-fill"), "progress bar present");
            Assert.That(html, Does.Contain("sorcha-splash-status"), "status text present");
            Assert.That(html, Does.Contain("css/splash.css"), "splash.css linked");
            Assert.That(html, Does.Contain("js/splash.js"), "splash.js referenced");
        });
    }

    [Test]
    public async Task SplashStylesheet_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("css/splash.css"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "css/splash.css should be served");
    }

    [Test]
    public async Task SplashScript_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("js/splash.js"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "js/splash.js should be served");
    }

    [Test]
    public async Task Splash_RemovedAfterBlazorHydration()
    {
        // Navigating + waiting for Blazor proves two things at once: the splash
        // markup/script did not break boot, and Blazor's render into #app
        // removes the splash (it lives inside #app, which Blazor clears).
        await NavigateToWalletAndWaitForBlazorAsync();

        var count = await Page.Locator("#sorcha-splash").CountAsync();
        Assert.That(count, Is.Zero,
            "Splash should be gone once Blazor renders App into #app.");
    }

    [Test]
    public async Task Splash_ReducedMotion_StillBootsAndRemovesSplash()
    {
        // With reduced motion, splash.js draws a single static canvas frame and
        // runs no rAF loop. Boot must still complete and the splash must still
        // be removed — i.e. the reduced-motion branch doesn't wedge startup.
        await Page.EmulateMediaAsync(new() { ReducedMotion = Microsoft.Playwright.ReducedMotion.Reduce });
        await NavigateToWalletAndWaitForBlazorAsync();

        var count = await Page.Locator("#sorcha-splash").CountAsync();
        Assert.That(count, Is.Zero,
            "Splash should be removed after hydration even under reduced motion.");
    }
}
