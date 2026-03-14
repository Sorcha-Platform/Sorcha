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
    /// Navigates to an authenticated page and verifies we weren't redirected to login.
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
