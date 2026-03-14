// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Login page (/auth/login).
/// This is a server-rendered Razor Page (not Blazor WASM).
/// </summary>
public class LoginPage
{
    private readonly IPage _page;

    public LoginPage(IPage page)
    {
        _page = page;
    }

    // Locators - matching the actual Razor Page form structure
    public ILocator EmailInput => _page.Locator("input[type='email']").First;
    public ILocator PasswordInput => _page.Locator("input[type='password']").First;
    public ILocator SignInButton => _page.Locator("button:has-text('Sign In')").First;
    public ILocator ErrorMessage => _page.Locator(".alert-danger");
    public ILocator LoginCard => _page.Locator(".auth-card");
    public ILocator LoginTitle => _page.Locator(".auth-header h2");
    public ILocator LoginSubtitle => _page.Locator(".auth-header p");
    public ILocator PasskeyButton => _page.Locator("#passkey-signin-btn");
    public ILocator SignUpLink => _page.Locator("a[href*='/auth/signup']");
    public ILocator ForgotPasswordLink => _page.Locator("a[href*='/auth/reset-password']");

    // 2FA locators (shown after initial login when 2FA is enabled)
    public ILocator TotpCodeInput => _page.Locator("input[inputmode='numeric']").First;
    public ILocator VerifyButton => _page.Locator("button:has-text('Verify')").First;

    // Legacy alias for backward compatibility with existing tests
    public ILocator UsernameInput => EmailInput;

    // Profile selector was removed - provide a no-op for backward compatibility
    public ILocator ProfileSelector => _page.Locator("select.nonexistent");

    /// <summary>
    /// Navigates to the login page and waits for it to load.
    /// The login page is server-rendered, so no WASM hydration needed.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Login}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the login form to be interactive.
    /// Returns true if the form loaded, false if it timed out.
    /// </summary>
    public async Task<bool> WaitForFormAsync()
    {
        try
        {
            await EmailInput.WaitForAsync(new() { Timeout = TestConstants.PageLoadTimeout });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Performs a complete login with the given credentials.
    /// The login form is a server-rendered POST form, so clicking Sign In
    /// triggers a full page navigation.
    /// </summary>
    public async Task LoginAsync(string email, string password, string? profileName = null)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);

        // Click triggers a form POST which navigates the page.
        // Use RunAndWaitForNavigationAsync pattern since form POST causes full navigation.
        // Don't wait for NetworkIdle as Blazor WASM boot keeps loading resources.
        await SignInButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    /// <summary>
    /// Performs login with the default test credentials.
    /// </summary>
    public async Task LoginWithTestCredentialsAsync()
    {
        await LoginAsync(
            TestConstants.TestEmail,
            TestConstants.TestPassword);
    }

    /// <summary>
    /// No-op: profile selector was removed from the login page.
    /// </summary>
    public Task SelectProfileAsync(string profileName) => Task.CompletedTask;

    /// <summary>
    /// No-op: profile selector was removed from the login page.
    /// </summary>
    public Task<IReadOnlyList<string>> GetProfileOptionsAsync()
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>
    /// Returns the current error message text, or null if no error is shown.
    /// </summary>
    public async Task<string?> GetErrorMessageAsync()
    {
        if (await ErrorMessage.CountAsync() > 0 && await ErrorMessage.IsVisibleAsync())
        {
            return await ErrorMessage.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Checks whether the login form is currently visible and interactive.
    /// </summary>
    public async Task<bool> IsFormVisibleAsync()
    {
        return await EmailInput.CountAsync() > 0
            && await EmailInput.IsVisibleAsync()
            && await PasswordInput.CountAsync() > 0
            && await PasswordInput.IsVisibleAsync();
    }
}
