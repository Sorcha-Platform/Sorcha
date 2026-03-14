// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests;

/// <summary>
/// End-to-end tests for dashboard layout and responsive design.
/// Logs in via the server-rendered login page, then tests the Blazor WASM dashboard.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Layout")]
public class DashboardLayoutTests : DockerTestBase
{
    private LoginPage _loginPage = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _loginPage = new LoginPage(Page);
    }

    /// <summary>
    /// Logs in and navigates to the dashboard. Returns false if login fails.
    /// </summary>
    private async Task<bool> LoginAndGoToDashboardAsync()
    {
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync())
        {
            TestContext.Out.WriteLine("Login form did not load");
            return false;
        }

        await _loginPage.LoginWithTestCredentialsAsync();

        try
        {
            await Page.WaitForURLAsync(
                url => !url.Contains("/auth/login"),
                new() { Timeout = TestConstants.PageLoadTimeout * 2 });
        }
        catch (TimeoutException)
        {
            var error = await _loginPage.GetErrorMessageAsync();
            TestContext.Out.WriteLine($"Login failed. Error: {error ?? "timeout"}");
            return false;
        }

        // Navigate to dashboard
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
        return true;
    }

    #region Dashboard Layout Tests

    [Test]
    [Retry(2)]
    public async Task Dashboard_LoadsAfterLogin()
    {
        if (!await LoginAndGoToDashboardAsync())
        {
            Assert.Fail("Login failed - cannot test dashboard");
            return;
        }

        await CaptureScreenshotAsync("after-login");

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty, "Dashboard should have content");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_LayoutInspection()
    {
        if (!await LoginAndGoToDashboardAsync())
        {
            Assert.Fail("Login failed - cannot test dashboard layout");
            return;
        }

        await CaptureScreenshotAsync("layout");

        // Check for common layout elements
        var appBar = Page.Locator("header, [role='banner'], .mud-appbar");
        var nav = Page.Locator("nav, [role='navigation'], .mud-drawer");
        var main = Page.Locator("main, [role='main'], .mud-main-content");

        var hasAppBar = await appBar.CountAsync() > 0;
        var hasNav = await nav.CountAsync() > 0;
        var hasMain = await main.CountAsync() > 0;

        TestContext.Out.WriteLine($"AppBar: {hasAppBar}, Nav: {hasNav}, Main: {hasMain}");

        var mudComponents = await Page.Locator("[class*='mud-']").CountAsync();
        TestContext.Out.WriteLine($"MudBlazor components found: {mudComponents}");

        Assert.That(mudComponents, Is.GreaterThan(0), "Dashboard should contain MudBlazor components");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_ResponsiveLayout_Desktop()
    {
        if (!await LoginAndGoToDashboardAsync()) { Assert.Fail("Login failed"); return; }

        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await CaptureScreenshotAsync("desktop-1920x1080");

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty, "Desktop layout should display content");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_ResponsiveLayout_Tablet()
    {
        if (!await LoginAndGoToDashboardAsync()) { Assert.Fail("Login failed"); return; }

        await Page.SetViewportSizeAsync(768, 1024);
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await CaptureScreenshotAsync("tablet-768x1024");

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty, "Tablet layout should display content");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_ResponsiveLayout_Mobile()
    {
        if (!await LoginAndGoToDashboardAsync()) { Assert.Fail("Login failed"); return; }

        await Page.SetViewportSizeAsync(375, 667);
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await CaptureScreenshotAsync("mobile-375x667");

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty, "Mobile layout should display content");
    }

    [Test]
    [Retry(2)]
    public async Task Dashboard_CSSAnalysis()
    {
        if (!await LoginAndGoToDashboardAsync()) { Assert.Fail("Login failed"); return; }

        var mudCount = await Page.Locator("[class*='mud-']").CountAsync();
        TestContext.Out.WriteLine($"MudBlazor components: {mudCount}");

        // This test is informational
        Assert.Pass("CSS analysis complete - check test output for details");
    }

    #endregion
}
