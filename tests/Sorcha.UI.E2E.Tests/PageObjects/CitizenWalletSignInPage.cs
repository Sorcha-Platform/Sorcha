// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Page object for the Citizen Wallet PWA sign-in screen
/// (<c>/wallet/signin</c>). Locators bind to <c>data-testid</c> attributes
/// on <c>Sorcha.Wallet.Pwa/Pages/SignIn.razor</c> so structural MudBlazor
/// markup changes do not break tests.
/// </summary>
public class CitizenWalletSignInPage
{
    private readonly IPage _page;

    /// <summary>Initialises the page object with the given Playwright page.</summary>
    public CitizenWalletSignInPage(IPage page)
    {
        _page = page;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Locators
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The outer sign-in stack container — always present on this route.</summary>
    public ILocator Screen => MudBlazorHelpers.TestId(_page, "signin-screen");

    /// <summary>Error alert — visible only when a sign-in attempt fails.</summary>
    public ILocator Error => MudBlazorHelpers.TestId(_page, "signin-error");

    /// <summary>Passkey sign-in button — visible only when WebAuthn is supported.</summary>
    public ILocator PasskeyButton => MudBlazorHelpers.TestId(_page, "signin-passkey");

    /// <summary>Email text field.</summary>
    public ILocator EmailInput => MudBlazorHelpers.TestId(_page, "signin-email");

    /// <summary>Password text field.</summary>
    public ILocator PasswordInput => MudBlazorHelpers.TestId(_page, "signin-password");

    /// <summary>Password-form submit button.</summary>
    public ILocator PasswordSubmitButton => MudBlazorHelpers.TestId(_page, "signin-password-submit");

    /// <summary>TOTP 2FA code input — visible only after a successful first-factor password check.</summary>
    public ILocator TwoFactorCodeInput => MudBlazorHelpers.TestId(_page, "signin-2fa-code");

    /// <summary>TOTP 2FA verify submit button.</summary>
    public ILocator TwoFactorSubmitButton => MudBlazorHelpers.TestId(_page, "signin-2fa-submit");

    /// <summary>
    /// Returns the button for the given social provider name (case-insensitive);
    /// e.g. <c>"Google"</c> → <c>data-testid="signin-social-google"</c>.
    /// </summary>
    public ILocator SocialButton(string provider) =>
        MudBlazorHelpers.TestId(_page, $"signin-social-{provider.ToLowerInvariant()}");

    // ─────────────────────────────────────────────────────────────────────────
    // Navigation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates directly to the wallet sign-in route and waits for the page to
    /// reach network-idle state. Does NOT wait for Blazor hydration — call
    /// <see cref="WaitForReadyAsync"/> afterwards if MudBlazor selectors are
    /// needed.
    /// </summary>
    public async Task GotoAsync()
    {
        var url = $"{TestConstants.UiWebUrl}{TestConstants.CitizenWalletBase}signin";
        await _page.GotoAsync(url);
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Waits for the <c>signin-screen</c> element to be visible — confirms
    /// Blazor WASM has hydrated and the sign-in UI has rendered.
    /// </summary>
    public async Task WaitForReadyAsync(int timeoutMs = TestConstants.PageLoadTimeout)
    {
        await Screen.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Actions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fills in the email/password fields and clicks the submit button.
    /// Does NOT wait for navigation — the caller is responsible for asserting
    /// the outcome (URL change, error, 2FA prompt, etc.).
    /// </summary>
    public async Task SignInWithPasswordAsync(string email, string password)
    {
        await EmailInput.FillAsync(email);
        await PasswordInput.FillAsync(password);
        await PasswordSubmitButton.ClickAsync();
    }
}
