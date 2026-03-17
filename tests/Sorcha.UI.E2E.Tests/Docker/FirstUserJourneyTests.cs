// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// End-to-end test for the complete first-user journey:
/// Landing page → Sign In → Login (single attempt) → Dashboard → Wallet wizard → Dashboard.
/// Tests the full unauthenticated-to-authenticated flow in a clean browser context.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Auth")]
[Category("FirstUserJourney")]
public class FirstUserJourneyTests : DockerTestBase
{
    /// <summary>
    /// No layout validation on login/landing pages (non-MudBlazor).
    /// Individual tests validate layout where appropriate.
    /// </summary>
    protected override bool ValidateLayoutHealth => false;

    #region Landing Page

    [Test]
    [Retry(2)]
    public async Task LandingPage_LoadsCorrectly()
    {
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);

        // Verify landing page content
        await Expect(Page).ToHaveTitleAsync(new Regex("Sorcha"));
        var signInLink = Page.Locator("a:has-text('Sign In')").First;
        Assert.That(await signInLink.IsVisibleAsync(), Is.True,
            "Sign In link should be visible on landing page");
    }

    [Test]
    [Retry(2)]
    public async Task LandingPage_SignInLink_NavigatesToLogin()
    {
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);

        var signInLink = Page.Locator("nav a:has-text('Sign In')").First;
        await signInLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        Assert.That(Page.Url, Does.Contain("/auth/login"),
            "Clicking Sign In should navigate to login page");
    }

    #endregion

    #region Login Flow - Token Bounce Fix

    [Test]
    [Retry(2)]
    public async Task Login_WithValidCredentials_AuthenticatesOnFirstAttempt()
    {
        // Navigate to login page
        await NavigateToAsync(TestConstants.PublicRoutes.Login);
        await Page.WaitForSelectorAsync("button:has-text('Sign In')",
            new() { Timeout = TestConstants.PageLoadTimeout });

        // Fill in credentials
        var emailInput = Page.Locator("input[type='email']").First;
        var passwordInput = Page.Locator("input[type='password']").First;
        await emailInput.FillAsync(TestConstants.TestEmail);
        await passwordInput.FillAsync(TestConstants.TestPassword);

        // Click Sign In
        await Page.Locator("button:has-text('Sign In')").First.ClickAsync();

        // Wait for navigation - should land on the app, NOT bounce back to login
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Give Blazor WASM time to boot and process the fragment token
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // The critical assertion: we should NOT be back on the login page
        Assert.That(IsOnLoginPage(), Is.False,
            "Login should succeed on the first attempt without bouncing back to the login page. " +
            $"Current URL: {Page.Url}");

        // We should be on an authenticated page (dashboard or app area)
        Assert.That(Page.Url, Does.Contain("/app/"),
            $"After login, user should be in the /app/ area. Current URL: {Page.Url}");
    }

    [Test]
    [Retry(2)]
    public async Task Login_LandsOnDashboard_WithWalletPrompt()
    {
        // Perform a fresh login
        await PerformFreshLoginAsync();

        // Should be on dashboard
        Assert.That(Page.Url, Does.Contain("/app/"),
            $"After login, should be in /app/ area. Current URL: {Page.Url}");

        // Wait for MudBlazor content to load
        await Page.WaitForSelectorAsync(".mud-layout",
            new() { Timeout = TestConstants.PageLoadTimeout });
    }

    #endregion

    #region Wallet Wizard Flow

    [Test]
    [Retry(2)]
    public async Task WalletWizard_FullFlow_RedirectsToDashboard()
    {
        // Start from a fresh login
        await PerformFreshLoginAsync();

        // Look for the CREATE WALLET button (first-login prompt)
        var createWalletButton = Page.Locator("a:has-text('CREATE WALLET'), button:has-text('CREATE WALLET')").First;

        // If there's a wallet prompt, use it; otherwise navigate to the wallet creation page
        if (await createWalletButton.IsVisibleAsync())
        {
            await createWalletButton.ClickAsync();
        }
        else
        {
            await Page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.WalletCreate}?wizard=true");
        }

        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Step 1: Fill in wallet name
        var walletNameInput = Page.Locator("input").GetByPlaceholder("Wallet Name");
        if (await walletNameInput.CountAsync() == 0)
        {
            // Try alternative selector
            walletNameInput = Page.Locator("[class*='mud-input'] input").First;
        }
        await walletNameInput.FillAsync("E2E Test Wallet");

        // Click CREATE WALLET button
        var createButton = Page.Locator("button:has-text('CREATE WALLET')").First;
        await createButton.ClickAsync();

        // Step 2: Mnemonic display - wait for recovery phrase
        await Page.WaitForSelectorAsync("text=Recovery Phrase",
            new() { Timeout = TestConstants.PageLoadTimeout });

        // Verify mnemonic words are shown
        var mnemonicText = await Page.TextContentAsync("body");
        Assert.That(mnemonicText, Does.Contain("Recovery Phrase"),
            "Mnemonic step should display the recovery phrase");

        // Check the confirmation checkboxes
        var checkbox1 = Page.Locator("input[type='checkbox']").Nth(0);
        var checkbox2 = Page.Locator("input[type='checkbox']").Nth(1);
        await checkbox1.CheckAsync(new() { Force = true });
        await checkbox2.CheckAsync(new() { Force = true });

        // Click CONTINUE TO WALLET
        var continueButton = Page.Locator("button:has-text('CONTINUE TO WALLET')").First;
        await continueButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await continueButton.ClickAsync();

        // Wait for navigation
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Critical assertion: should redirect to /app/dashboard, NOT to /
        Assert.That(Page.Url, Does.Not.Match(@"^https?://[^/]+/?$"),
            $"After wallet creation, should NOT redirect to the public landing page. Current URL: {Page.Url}");
        Assert.That(Page.Url, Does.Contain("/app/"),
            $"After wallet creation, should stay in the /app/ area. Current URL: {Page.Url}");

        // Verify we're on an authenticated page (not the landing page)
        var isLandingPage = await Page.Locator("text=Quantum-Safe Trust Infrastructure").IsVisibleAsync();
        Assert.That(isLandingPage, Is.False,
            "Should not be on the public landing page after wallet creation");
    }

    #endregion

    #region i18n Translation

    [Test]
    [Retry(2)]
    public async Task Navigation_EncryptionOperations_ShowsTranslatedLabel()
    {
        // Use the global auth state to access an authenticated page
        if (!GlobalAuthSetup.AuthSucceeded)
        {
            Assert.Ignore("Global authentication setup failed - skipping");
        }

        // Create an authenticated context
        var contextOptions = new BrowserNewContextOptions();
        if (GlobalAuthSetup.StorageStatePath != null &&
            File.Exists(GlobalAuthSetup.StorageStatePath))
        {
            contextOptions.StorageStatePath = GlobalAuthSetup.StorageStatePath;
        }

        await using var context = await Browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // Check that the nav doesn't show the raw translation key
        var rawKeyVisible = await page.Locator("text=nav.encryptionOperations").IsVisibleAsync();
        Assert.That(rawKeyVisible, Is.False,
            "Navigation should show translated 'Encryption Operations' text, not the raw i18n key 'nav.encryptionOperations'");

        // Check that the translated label is visible
        var translatedVisible = await page.Locator("text=Encryption Operations").IsVisibleAsync();
        Assert.That(translatedVisible, Is.True,
            "Navigation should display 'Encryption Operations' in the sidebar");
    }

    #endregion

    #region Full Journey (Landing → Login → Wallet → Dashboard)

    [Test]
    [Retry(2)]
    public async Task FullFirstUserJourney_LandingToWalletCreation()
    {
        // Step 1: Start at landing page
        await NavigateToAsync(TestConstants.PublicRoutes.Landing);
        await Expect(Page).ToHaveTitleAsync(new Regex("Sorcha"));

        // Step 2: Click Sign In
        var signInLink = Page.Locator("nav a:has-text('Sign In')").First;
        await signInLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.That(Page.Url, Does.Contain("/auth/login"), "Should navigate to login page");

        // Step 3: Enter credentials and submit
        await Page.WaitForSelectorAsync("button:has-text('Sign In')",
            new() { Timeout = TestConstants.PageLoadTimeout });
        await Page.Locator("input[type='email']").First.FillAsync(TestConstants.TestEmail);
        await Page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);
        await Page.Locator("button:has-text('Sign In')").First.ClickAsync();

        // Step 4: Should arrive at authenticated area on FIRST attempt
        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        Assert.That(IsOnLoginPage(), Is.False,
            $"Login should succeed on first attempt. Current URL: {Page.Url}");
        Assert.That(Page.Url, Does.Contain("/app/"),
            $"Should be in authenticated area. Current URL: {Page.Url}");

        // Step 5: Verify dashboard loaded with navigation
        await Page.WaitForSelectorAsync(".mud-layout",
            new() { Timeout = TestConstants.PageLoadTimeout });
        var navVisible = await Page.Locator(".mud-navmenu").IsVisibleAsync();
        Assert.That(navVisible, Is.True, "Navigation menu should be visible after login");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Performs a fresh login from the login page in a clean browser context.
    /// Asserts that login succeeds on the first attempt (no token bounce).
    /// </summary>
    private async Task PerformFreshLoginAsync()
    {
        await NavigateToAsync(TestConstants.PublicRoutes.Login);
        await Page.WaitForSelectorAsync("button:has-text('Sign In')",
            new() { Timeout = TestConstants.PageLoadTimeout });

        await Page.Locator("input[type='email']").First.FillAsync(TestConstants.TestEmail);
        await Page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);
        await Page.Locator("button:has-text('Sign In')").First.ClickAsync();

        await Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);
    }

    #endregion
}
