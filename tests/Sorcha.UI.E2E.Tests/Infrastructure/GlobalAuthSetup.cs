// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests;

/// <summary>
/// Global setup fixture that runs ONCE before all test classes.
/// Performs a single login and saves browser storage state (cookies + localStorage)
/// for reuse by all authenticated test fixtures.
/// </summary>
[SetUpFixture]
public class GlobalAuthSetup
{
    /// <summary>
    /// Path to the saved browser storage state file containing auth tokens.
    /// </summary>
    public static string? StorageStatePath { get; private set; }

    /// <summary>
    /// Whether the global auth setup succeeded.
    /// </summary>
    public static bool AuthSucceeded { get; private set; }

    /// <summary>
    /// Timestamp of the last successful authentication.
    /// </summary>
    public static DateTime AuthTimestamp { get; private set; }

    [OneTimeSetUp]
    public async Task GlobalSetUp()
    {
        // Retry auth up to 3 times to handle token bounce race condition
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await PerformLoginAsync();
                if (AuthSucceeded) break;

                TestContext.Out.WriteLine($"Auth attempt {attempt} failed, retrying...");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine($"Auth attempt {attempt} error: {ex.Message}");
                if (attempt == 3) throw;
            }
        }

        if (!AuthSucceeded)
        {
            TestContext.Out.WriteLine("WARNING: Global auth setup failed after 3 attempts. " +
                "All authenticated tests will be skipped.");
        }
    }

    [OneTimeTearDown]
    public void GlobalTearDown()
    {
        // Clean up temp storage state file
        if (StorageStatePath != null && File.Exists(StorageStatePath))
        {
            try { File.Delete(StorageStatePath); } catch { /* ignore */ }
        }
    }

    private static async Task PerformLoginAsync()
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            // Navigate to server-rendered login page
            await page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Login}");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Wait for login form
            var emailInput = page.Locator("input[type='email']").First;
            await emailInput.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });

            // Fill credentials and submit
            await emailInput.FillAsync(TestConstants.TestEmail);
            await page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);
            await page.Locator("button:has-text('Sign In')").First.ClickAsync();

            // Wait for redirect away from login
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var loginSucceeded = false;
            try
            {
                await page.WaitForURLAsync(
                    url => !url.Contains("/auth/login"),
                    new() { Timeout = TestConstants.PageLoadTimeout * 2 });
                loginSucceeded = true;
            }
            catch (TimeoutException)
            {
                // Token bounce — check if token was issued
                loginSucceeded = page.Url.Contains("token");
            }

            if (!loginSucceeded)
            {
                TestContext.Out.WriteLine($"Login failed. URL: {page.Url}");
                return;
            }

            // Wait for Blazor to process the token fragment
            await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout * 2);

            // Navigate to dashboard to verify auth and let WASM fully load
            await page.GotoAsync(
                $"{TestConstants.UiWebUrl}{TestConstants.AuthenticatedRoutes.Dashboard}");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

            // Verify we're authenticated
            if (page.Url.Contains("/auth/login"))
            {
                TestContext.Out.WriteLine(
                    $"Auth bounce detected after dashboard nav. URL: {page.Url}");
                return;
            }

            // Save storage state
            var statePath = Path.Combine(
                Path.GetTempPath(),
                $"sorcha-e2e-auth-{DateTime.Now:yyyyMMdd_HHmmss}.json");

            await context.StorageStateAsync(new() { Path = statePath });
            StorageStatePath = statePath;
            AuthSucceeded = true;
            AuthTimestamp = DateTime.UtcNow;

            TestContext.Out.WriteLine($"Global auth setup succeeded. State: {statePath}");
        }
        finally
        {
            await context.CloseAsync();
            await browser.CloseAsync();
            playwright.Dispose();
        }
    }
}
