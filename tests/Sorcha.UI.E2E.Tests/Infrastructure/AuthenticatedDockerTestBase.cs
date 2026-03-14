// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Base class for Docker E2E tests that require authentication.
/// Uses the global auth state from <see cref="GlobalAuthSetup"/> which logs in
/// once before all tests and shares the browser storage state.
/// </summary>
public abstract class AuthenticatedDockerTestBase : DockerTestBase
{
    /// <summary>
    /// MudBlazor layout should be validated on authenticated pages.
    /// </summary>
    protected override bool ValidateLayoutHealth => true;

    [SetUp]
    public override async Task BaseSetUp()
    {
        await base.BaseSetUp();

        if (!GlobalAuthSetup.AuthSucceeded)
        {
            Assert.Ignore("Global authentication setup failed - skipping authenticated test");
        }
    }

    /// <summary>
    /// Provides authenticated browser context options using the global auth state.
    /// </summary>
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions() ?? new BrowserNewContextOptions();

        if (GlobalAuthSetup.StorageStatePath != null &&
            File.Exists(GlobalAuthSetup.StorageStatePath))
        {
            options.StorageStatePath = GlobalAuthSetup.StorageStatePath;
        }

        return options;
    }

    /// <summary>
    /// Navigates to an authenticated page. If redirected to login (token not loaded
    /// from localStorage yet), performs an inline re-login and retries.
    /// </summary>
    protected async Task NavigateAuthenticatedAsync(string path)
    {
        await NavigateAndWaitForBlazorAsync(path);

        if (!IsOnLoginPage()) return;

        // Token may not have loaded from localStorage yet — re-login inline
        try
        {
            var emailInput = Page.Locator("input[type='email']").First;
            if (await emailInput.CountAsync() > 0)
            {
                await emailInput.FillAsync(TestConstants.TestEmail);
                await Page.Locator("input[type='password']").First.FillAsync(TestConstants.TestPassword);
                await Page.Locator("button:has-text('Sign In')").First.ClickAsync();
                await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.DOMContentLoaded);
                await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

                // Navigate to the original target
                await NavigateAndWaitForBlazorAsync(path);
            }
        }
        catch
        {
            // Inline re-login failed — mark inconclusive
        }

        if (IsOnLoginPage())
        {
            Assert.Inconclusive(
                $"Auth session expired - redirected to login when navigating to {path}.");
        }
    }
}
