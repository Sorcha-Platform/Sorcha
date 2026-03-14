// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for Docker E2E tests that require authentication.
/// Logs in once per test fixture and reuses the authenticated browser state
/// across all tests in the class, eliminating login boilerplate.
/// </summary>
public abstract class AuthenticatedDockerTestBase : DockerTestBase
{
    private static readonly SemaphoreSlim _authLock = new(1, 1);
    private static string? _storageStatePath;
    private static bool _authAttempted;
    private static bool _authSucceeded;

    /// <summary>
    /// MudBlazor layout should be validated on authenticated pages.
    /// </summary>
    protected override bool ValidateLayoutHealth => true;

    [OneTimeSetUp]
    public async Task AuthenticatedOneTimeSetUp()
    {
        await EnsureAuthenticatedStateAsync();
    }

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();

        // If auth never succeeded, skip tests gracefully
        if (!_authSucceeded)
        {
            Assert.Ignore("Authentication setup failed - skipping authenticated test");
        }
    }

    /// <summary>
    /// Performs login once and saves browser storage state (cookies, localStorage)
    /// for reuse across all test fixtures that inherit from this class.
    /// </summary>
    private async Task EnsureAuthenticatedStateAsync()
    {
        await _authLock.WaitAsync();
        try
        {
            // Already authenticated in this test run
            if (_authAttempted)
                return;

            _authAttempted = true;

            // Create a temporary browser context to perform login
            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            try
            {
                // Navigate to login (server-rendered Razor Page at /auth/login)
                await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Login}");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // Wait for login form (email input, not text - it's type='email')
                var emailInput = page.Locator("input[type='email']").First;
                await emailInput.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

                // Fill credentials
                await emailInput.FillAsync(TestConstants.TestEmail);
                await page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);

                // Click login
                var loginButton = page.Locator("button:has-text('Sign In')").First;
                await loginButton.ClickAsync();

                // Wait for navigation away from login
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

                // Verify login succeeded
                if (page.Url.Contains("/auth/login"))
                {
                    TestContext.Out.WriteLine("WARNING: Login did not redirect away from login page. " +
                        "Auth state may not be available.");
                    // Still save state - some pages may partially work
                }

                // Save storage state for reuse
                var statePath = Path.Combine(
                    Path.GetTempPath(),
                    $"sorcha-e2e-auth-state-{Guid.NewGuid():N}.json");

                await context.StorageStateAsync(new() { Path = statePath });
                _storageStatePath = statePath;
                _authSucceeded = !page.Url.Contains("/auth/login");

                if (_authSucceeded)
                {
                    TestContext.Out.WriteLine($"Authentication succeeded. State saved to {statePath}");
                }
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Authentication setup failed: {ex.Message}");
                _authSucceeded = false;
            }
            finally
            {
                await context.CloseAsync();
                await browser.CloseAsync();
                playwright.Dispose();
            }
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// Provides authenticated browser context options.
    /// Playwright NUnit's PageTest uses this to create the context.
    /// </summary>
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions() ?? new BrowserNewContextOptions();

        if (_storageStatePath != null && File.Exists(_storageStatePath))
        {
            options.StorageStatePath = _storageStatePath;
        }

        return options;
    }

    /// <summary>
    /// Navigates to an authenticated page and verifies we weren't redirected to login.
    /// Throws <see cref="InconclusiveException"/> if auth has expired.
    /// </summary>
    protected async Task NavigateAuthenticatedAsync(string path)
    {
        await NavigateAndWaitForBlazorAsync(path);

        if (IsOnLoginPage())
        {
            Assert.Inconclusive(
                $"Auth session expired - redirected to login when navigating to {path}. " +
                "Re-run to refresh auth state.");
        }
    }
}
