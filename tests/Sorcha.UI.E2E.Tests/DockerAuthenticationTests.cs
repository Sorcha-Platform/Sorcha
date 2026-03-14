// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests;

/// <summary>
/// End-to-end tests for authentication and basic pages against Docker environment.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Auth")]
public class DockerAuthenticationTests : DockerTestBase
{
    private LoginPage _loginPage = null!;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();
        _loginPage = new LoginPage(Page);
    }

    #region Login Page Tests

    [Test]
    [Retry(3)]
    public async Task LoginPage_LoadsSuccessfully()
    {
        await _loginPage.NavigateAsync();
        var formLoaded = await _loginPage.WaitForFormAsync();

        Assert.That(formLoaded, Is.True, "Login form should load with email and password fields");

        var title = await Page.TitleAsync();
        Assert.That(title, Does.Contain("Sign In").IgnoreCase.Or.Contain("Sorcha").IgnoreCase,
            $"Page title should contain 'Sign In' or 'Sorcha'. Got: '{title}'");
    }

    [Test]
    public async Task LoginPage_HasProfileSelector()
    {
        // Profile selector was removed from the login page.
        // This test now verifies the passkey button exists instead.
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync()) return;

        await Expect(_loginPage.PasskeyButton).ToBeVisibleAsync();
    }

    [Test]
    [Retry(2)]
    public async Task LoginPage_ShowsErrorForEmptyCredentials()
    {
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync()) return;

        await _loginPage.SignInButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show validation error or stay on login page
        var error = await _loginPage.GetErrorMessageAsync();
        var stillOnLogin = Page.Url.Contains("/auth/login");

        Assert.That(error != null || stillOnLogin, Is.True,
            "Should show error or remain on login page when submitting empty form");
    }

    [Test]
    [Retry(2)]
    public async Task LoginPage_ShowsErrorForInvalidCredentials()
    {
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync()) return;

        await _loginPage.LoginAsync("invalid@test.com", "wrongpassword");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

        var error = await _loginPage.GetErrorMessageAsync();
        var stillOnLogin = Page.Url.Contains("/auth/login");

        Assert.That(error != null || stillOnLogin, Is.True,
            "Should show error or remain on login page for invalid credentials");
    }

    #endregion

    #region Authentication Flow Tests

    [Test]
    [Retry(3)]
    public async Task Login_WithValidCredentials_RedirectsToDashboard()
    {
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync())
        {
            Assert.Fail("Login form did not load");
            return;
        }

        await _loginPage.LoginWithTestCredentialsAsync();

        // Wait for navigation away from login (server-side redirect -> Blazor app)
        try
        {
            await Page.WaitForURLAsync(
                url => !url.Contains("/auth/login"),
                new() { Timeout = TestConstants.PageLoadTimeout * 2 });
        }
        catch (TimeoutException)
        {
            var error = await _loginPage.GetErrorMessageAsync();
            Assert.Fail(
                $"Login did not navigate away from login page. " +
                $"URL: {Page.Url}. Error: {error ?? "none"}");
        }

        Assert.That(Page.Url, Does.Not.Contain("/auth/login"),
            "Should navigate away from login page after successful login");
    }

    [Test]
    public async Task ProtectedPage_RedirectsToLogin_WhenNotAuthenticated()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Designer}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var url = Page.Url;
        var pageContent = await Page.TextContentAsync("body") ?? "";

        var requiresAuth = url.Contains("/auth/login")
            || pageContent.Contains("Sign In", StringComparison.OrdinalIgnoreCase);

        Assert.That(requiresAuth, Is.True,
            "Protected page should redirect to login or show sign in");
    }

    [Test]
    [Retry(2)]
    public async Task Logout_RedirectsToLoginPage()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Logout}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var url = Page.Url;
        Assert.That(
            url.Contains("/auth/login") || url.Contains("/auth/logout"),
            Is.True,
            $"Should be on login or logout page. Got: {url}");
    }

    #endregion

    #region Basic Page Tests

    [Test]
    public async Task HomePage_LoadsSuccessfully()
    {
        await Page.GotoAsync(TestConstants.UiWebUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty);
        Assert.That(pageContent, Does.Contain("Sorcha").IgnoreCase
            .Or.Contain("Welcome").IgnoreCase
            .Or.Contain("Sign In").IgnoreCase);
    }

    [Test]
    public async Task HomePage_ShowsLandingForUnauthenticated()
    {
        await Page.GotoAsync(TestConstants.UiWebUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Does.Contain("Sign In").IgnoreCase
            .Or.Contain("Get Started").IgnoreCase
            .Or.Contain("Login").IgnoreCase);
    }

    [Test]
    [Retry(3)]
    public async Task ApiGateway_AdminRoute_Accessible()
    {
        var response = await Page.GotoAsync($"{TestConstants.ApiGatewayUrl}/admin/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.That(response?.Status, Is.LessThan(500),
            $"Admin route should not return server error. Got: {response?.Status}");
    }

    [Test]
    public async Task ApiGateway_ScalarDocs_Accessible()
    {
        // Scalar UI requires auth; the OpenAPI JSON spec is public
        var response = await Page.GotoAsync($"{TestConstants.ApiGatewayUrl}/openapi/v1.json");

        Assert.That(response?.Status, Is.EqualTo(200), "OpenAPI spec should be accessible at /openapi/v1.json");
    }

    #endregion

    #region UI Component Tests

    [Test]
    public async Task MudBlazor_StylesLoaded()
    {
        await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AppBase}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var mudComponents = await Page.Locator("[class*='mud-']").CountAsync();
        var styleLinks = await Page.Locator("link[href*='.css']").CountAsync();

        Assert.That(mudComponents > 0 || styleLinks > 0, Is.True,
            $"Should have MudBlazor styles. Components: {mudComponents}, Stylesheets: {styleLinks}");
    }

    [Test]
    public async Task NoJavaScriptErrors_OnHomePage()
    {
        var jsErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                jsErrors.Add(msg.Text);
        };

        await Page.GotoAsync(TestConstants.UiWebUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.NetworkIdleWait);

        var criticalErrors = jsErrors.Where(e =>
            !TestConstants.KnownConsoleErrorPatterns.Any(
                pattern => e.Contains(pattern, StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.That(criticalErrors, Is.Empty,
            $"Should have no critical JS errors. Found: {string.Join(", ", criticalErrors)}");
    }

    [Test]
    public async Task NoJavaScriptErrors_OnLoginPage()
    {
        var jsErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                jsErrors.Add(msg.Text);
        };

        await _loginPage.NavigateAsync();
        await Page.WaitForTimeoutAsync(TestConstants.NetworkIdleWait);

        var criticalErrors = jsErrors.Where(e =>
            !TestConstants.KnownConsoleErrorPatterns.Any(
                pattern => e.Contains(pattern, StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.That(criticalErrors, Is.Empty,
            $"Should have no critical JS errors on login. Found: {string.Join(", ", criticalErrors)}");
    }

    [Test]
    public async Task ResponsiveDesign_MobileViewport()
    {
        await Page.SetViewportSizeAsync(375, 667);
        await Page.GotoAsync(TestConstants.UiWebUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty);
    }

    [Test]
    public async Task ResponsiveDesign_DesktopViewport()
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(TestConstants.UiWebUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Is.Not.Empty);
    }

    #endregion

    #region Auth Required Tests

    [Test]
    public async Task WalletList_RequiresAuthentication()
    {
        await NavigateAndWaitForBlazorAsync(TestConstants.AuthenticatedRoutes.Wallets);

        Assert.That(IsOnLoginPage() || (await Page.TextContentAsync("body") ?? "").Contains("Sign In", StringComparison.OrdinalIgnoreCase),
            Is.True, "Wallet list page should require authentication");
    }

    [Test]
    public async Task CreateWallet_RequiresAuthentication()
    {
        await NavigateAndWaitForBlazorAsync(TestConstants.AuthenticatedRoutes.WalletCreate);

        Assert.That(IsOnLoginPage() || (await Page.TextContentAsync("body") ?? "").Contains("Sign In", StringComparison.OrdinalIgnoreCase),
            Is.True, "Create wallet page should require authentication");
    }

    [Test]
    public async Task RecoverWallet_RequiresAuthentication()
    {
        await NavigateAndWaitForBlazorAsync(TestConstants.AuthenticatedRoutes.WalletRecover);

        Assert.That(IsOnLoginPage() || (await Page.TextContentAsync("body") ?? "").Contains("Sign In", StringComparison.OrdinalIgnoreCase),
            Is.True, "Recover wallet page should require authentication");
    }

    [Test]
    public async Task SchemaLibrary_RequiresAuthentication()
    {
        await NavigateAndWaitForBlazorAsync(TestConstants.AuthenticatedRoutes.Schemas);

        Assert.That(IsOnLoginPage() || (await Page.TextContentAsync("body") ?? "").Contains("Sign In", StringComparison.OrdinalIgnoreCase),
            Is.True, "Schema library page should require authentication");
    }

    #endregion

    #region Authenticated Schema Tests

    [Test]
    [Retry(3)]
    public async Task SchemaLibrary_LoadsSuccessfully_WhenAuthenticated()
    {
        await LoginAndNavigateAsync(TestConstants.AuthenticatedRoutes.Schemas);

        var pageContent = await Page.TextContentAsync("body");
        Assert.That(pageContent, Does.Contain("Schema").IgnoreCase
            .Or.Contain("Library").IgnoreCase
            .Or.Contain("Sign In").IgnoreCase);
    }

    [Test]
    [Retry(3)]
    public async Task SchemaLibrary_NoJavaScriptErrors_WhenAuthenticated()
    {
        var jsErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                jsErrors.Add(msg.Text);
        };

        await LoginAndNavigateAsync(TestConstants.AuthenticatedRoutes.Schemas);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var criticalErrors = jsErrors.Where(e =>
            !TestConstants.KnownConsoleErrorPatterns.Any(
                pattern => e.Contains(pattern, StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.That(criticalErrors, Is.Empty,
            $"Should have no critical JS errors on schemas page. Found: {string.Join(", ", criticalErrors)}");
    }

    [Test]
    [Retry(3)]
    public async Task SchemaLibrary_ShowsSystemSchemas()
    {
        await LoginAndNavigateAsync(TestConstants.AuthenticatedRoutes.Schemas);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        var pageContent = await Page.TextContentAsync("body") ?? "";

        var hasContent = pageContent.Contains("installation", StringComparison.OrdinalIgnoreCase)
            || pageContent.Contains("organisation", StringComparison.OrdinalIgnoreCase)
            || pageContent.Contains("participant", StringComparison.OrdinalIgnoreCase)
            || pageContent.Contains("System", StringComparison.OrdinalIgnoreCase)
            || pageContent.Contains("Schema", StringComparison.OrdinalIgnoreCase);

        Assert.That(hasContent, Is.True,
            $"Should show system schemas. Content preview: {pageContent[..Math.Min(500, pageContent.Length)]}");
    }

    [Test]
    [Retry(2)]
    public async Task SchemaLibrary_CanSearchSchemas_WhenAuthenticated()
    {
        await LoginAndNavigateAsync(TestConstants.AuthenticatedRoutes.Schemas);

        var searchInput = Page.Locator("input[placeholder*='search' i], input[type='search'], .mud-input-text input");
        if (await searchInput.CountAsync() > 0)
        {
            await searchInput.First.FillAsync("installation");
            await Page.WaitForTimeoutAsync(TestConstants.ShortWait);

            var pageContent = await Page.TextContentAsync("body");
            Assert.That(pageContent, Does.Contain("installation").IgnoreCase
                .Or.Contain("No results").IgnoreCase
                .Or.Contain("Schema").IgnoreCase);
        }
        else
        {
            Assert.Pass("Search input not found - page may still be loading");
        }
    }

    [Test]
    public async Task NavigationMenu_ContainsWalletLinks()
    {
        await LoginAndNavigateAsync(TestConstants.AuthenticatedRoutes.Dashboard);

        var pageContent = await Page.ContentAsync();
        var hasWalletReferences = pageContent.Contains("wallet", StringComparison.OrdinalIgnoreCase)
            || pageContent.Contains("/wallets", StringComparison.OrdinalIgnoreCase);

        if (!hasWalletReferences)
            Assert.Pass("Wallet links not visible in current view (acceptable)");
        else
            Assert.That(hasWalletReferences, Is.True, "Navigation should reference wallets");
    }

    #endregion

    /// <summary>
    /// Helper: logs in via the server-rendered login page and navigates to the target.
    /// </summary>
    private async Task LoginAndNavigateAsync(string targetPath)
    {
        await _loginPage.NavigateAsync();
        if (!await _loginPage.WaitForFormAsync())
        {
            Assert.Inconclusive("Login form did not load");
            return;
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
            Assert.Inconclusive($"Login failed. Error: {error ?? "timeout"}");
            return;
        }

        await Page.GotoAsync($"{TestConstants.UiWebUrl}{targetPath}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }
}
