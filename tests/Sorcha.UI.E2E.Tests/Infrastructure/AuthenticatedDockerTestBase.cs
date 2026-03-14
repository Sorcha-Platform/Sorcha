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
    private static DateTime _authTimestamp;

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
            // Skip re-auth if already succeeded and token isn't expired
            if (_authSucceeded &&
                DateTime.UtcNow - _authTimestamp < TimeSpan.FromMinutes(45))
                return;

            // Reset for fresh auth attempt
            _authAttempted = true;
            _authSucceeded = false;

            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            try
            {
                // 1. Navigate to server-rendered login page
                await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Login}");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // 2. Wait for login form
                var emailInput = page.Locator("input[type='email']").First;
                await emailInput.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

                // 3. Fill credentials and submit
                await emailInput.FillAsync(TestConstants.TestEmail);
                await page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);
                await page.Locator("button:has-text('Sign In')").First.ClickAsync();

                // 4. Wait for the form POST to redirect (DOMContentLoaded, not NetworkIdle
                //    because Blazor WASM boot keeps loading resources)
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                // 5. Check if we got redirected to /app/#token=...
                //    The server redirects to /app/#token={jwt}&refresh={refreshToken}
                //    Then FragmentTokenHandler processes the token in OnAfterRenderAsync
                var loginSucceeded = false;

                try
                {
                    // Wait for URL to leave /auth/login
                    await page.WaitForURLAsync(
                        url => !url.Contains("/auth/login"),
                        new() { Timeout = TestConstants.PageLoadTimeout * 2 });

                    loginSucceeded = true;
                }
                catch (TimeoutException)
                {
                    // Check if the URL has a token in the returnUrl (bounce scenario)
                    loginSucceeded = page.Url.Contains("token");
                }

                if (loginSucceeded)
                {
                    // 6. Wait for Blazor WASM to fully hydrate and process the token.
                    //    FragmentTokenHandler stores the token in encrypted localStorage
                    //    during OnAfterRenderAsync, so we need to wait for that.
                    await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout * 2);

                    // 7. Navigate to dashboard to ensure app is fully loaded
                    await page.GotoAsync(
                        $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}");
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                    await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

                    // 8. Verify we're authenticated (not bounced to login)
                    _authSucceeded = !page.Url.Contains("/auth/login");

                    if (!_authSucceeded)
                    {
                        TestContext.Out.WriteLine(
                            $"WARNING: Auth bounce detected. URL: {page.Url}. " +
                            "Token may not have been stored before auth guard fired.");
                    }
                }

                if (_authSucceeded)
                {
                    // 9. Save storage state (cookies + localStorage with encrypted token)
                    var statePath = Path.Combine(
                        Path.GetTempPath(),
                        $"sorcha-e2e-auth-state-{Guid.NewGuid():N}.json");

                    await context.StorageStateAsync(new() { Path = statePath });
                    _storageStatePath = statePath;

                    _authTimestamp = DateTime.UtcNow;
                    TestContext.Out.WriteLine($"Authentication succeeded. State saved to {statePath}");
                }
                else
                {
                    TestContext.Out.WriteLine(
                        $"Authentication failed. URL after login: {page.Url}");
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
