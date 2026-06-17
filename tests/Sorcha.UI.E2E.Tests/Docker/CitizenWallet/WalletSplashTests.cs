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
        });
    }

    [Test]
    public async Task SplashStylesheet_IsServed()
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(WalletUrl("css/splash.css"));
        Assert.That((int)res.StatusCode, Is.EqualTo(200), "css/splash.css should be served");
    }
}
