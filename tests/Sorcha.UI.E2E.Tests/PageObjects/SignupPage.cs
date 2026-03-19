// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Signup page (/auth/signup).
/// This is a server-rendered Razor Page with tabs for Passkey, Social, and Email signup.
/// </summary>
public class SignupPage
{
    private readonly IPage _page;

    public SignupPage(IPage page)
    {
        _page = page;
    }

    // Tab selectors — the signup page has 3 method-tab buttons
    public ILocator PasskeyTab => _page.Locator(".method-tab[data-tab='passkey']").First;
    public ILocator SocialTab => _page.Locator(".method-tab[data-tab='social']").First;
    public ILocator EmailTab => _page.Locator(".method-tab[data-tab='email']").First;

    // Email signup form fields (inside #tab-email — use asp-for IDs to avoid passkey tab's fields)
    public ILocator DisplayNameInput => _page.Locator("#tab-email input#DisplayName").First;
    public ILocator EmailInput => _page.Locator("#tab-email input#Email").First;
    public ILocator PasswordInput => _page.Locator("#tab-email input#Password").First;
    public ILocator ConfirmPasswordInput => _page.Locator("#tab-email input#ConfirmPassword").First;
    public ILocator SignUpButton => _page.Locator("#tab-email button[type='submit']").First;

    // Feedback
    public ILocator ErrorMessage => _page.Locator(".alert-danger, .validation-summary-errors");
    public ILocator SuccessMessage => _page.Locator(".alert-success");
    public ILocator SignupCard => _page.Locator(".auth-card");
    public ILocator SignupTitle => _page.Locator(".auth-header h2");

    // Links
    public ILocator SignInLink => _page.Locator("a[href*='/auth/login']");

    /// <summary>
    /// Navigates to the signup page.
    /// </summary>
    public async Task NavigateAsync()
    {
        await _page.GotoAsync($"{TestConstants.UiWebUrl}{TestConstants.PublicRoutes.Signup}");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the signup form to be interactive.
    /// </summary>
    public async Task<bool> WaitForFormAsync()
    {
        try
        {
            // The email input might be on the Email tab — try clicking it first
            await _page.WaitForTimeoutAsync(1000);
            return await SignupCard.CountAsync() > 0;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Switches to the Email signup tab.
    /// </summary>
    public async Task SelectEmailTabAsync()
    {
        if (await EmailTab.CountAsync() > 0)
        {
            await EmailTab.ClickAsync();
            // Wait for the email tab content to become visible
            await _page.Locator("#tab-email").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = TestConstants.ElementTimeout });
        }
    }

    /// <summary>
    /// Performs a complete email signup.
    /// </summary>
    public async Task SignUpWithEmailAsync(string displayName, string email, string password)
    {
        await SelectEmailTabAsync();

        // Fill form fields
        if (await DisplayNameInput.CountAsync() > 0)
        {
            await DisplayNameInput.FillAsync(displayName);
        }

        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);

        if (await ConfirmPasswordInput.CountAsync() > 0)
        {
            await ConfirmPasswordInput.FillAsync(password);
        }

        await SignUpButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

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
    /// Returns the current success message text, or null if none shown.
    /// </summary>
    public async Task<string?> GetSuccessMessageAsync()
    {
        if (await SuccessMessage.CountAsync() > 0 && await SuccessMessage.IsVisibleAsync())
        {
            return await SuccessMessage.TextContentAsync();
        }
        return null;
    }

    /// <summary>
    /// Checks whether the signup form is currently visible.
    /// </summary>
    public async Task<bool> IsFormVisibleAsync()
    {
        return await SignupCard.CountAsync() > 0 && await SignupCard.IsVisibleAsync();
    }
}
