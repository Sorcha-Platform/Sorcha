// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// E2E coverage for the Citizen Wallet PWA sign-in screen
/// (<c>/wallet/signin</c>): auth-gate redirect, password happy-path,
/// social OAuth fragment return, and passkey-option visibility.
/// </summary>
/// <remarks>
/// <para>
/// <b>Runnable without seeding (CI-safe):</b>
/// <c>SignedOut_ProtectedRoute_RedirectsToSignIn</c> — navigates anonymously
/// to a protected route and asserts the PWA's <c>RedirectToSignIn</c>
/// component sends the browser to <c>/wallet/signin</c>.
/// </para>
/// <para>
/// <b>Requires Docker stack + seeded citizen account:</b>
/// <c>Password_HappyPath_SignsInAndLeavesSignInScreen</c> and
/// <c>SocialReturnLeg_StoresTokenAndLeavesSignIn</c> require a real citizen
/// user in the database. Those tests are marked <c>[Explicit]</c> so they are
/// excluded from the normal CI run but can be executed locally against a
/// running Docker stack by passing <c>--explicit</c> to the test runner.
/// </para>
/// <para>
/// <b>Requires a seeded credential for the full ceremony:</b>
/// <c>Passkey_VirtualAuthenticator_ShowsPasskeyButton</c> uses the Playwright
/// CDP virtual authenticator API to enable WebAuthn in the browser and asserts
/// the passkey button becomes visible. The full sign-in ceremony is
/// <c>[Explicit]</c> because it requires a server-seeded WebAuthn credential.
/// </para>
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
[Category("Auth")]
public class CitizenWalletSignInTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletSignInPage SignInPage => new(Page);

    // ─────────────────────────────────────────────────────────────────────────
    // Gate-redirect — fully deterministic, no seeding required
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An anonymous visit to a protected wallet route (e.g. <c>/wallet/devices</c>)
    /// must be redirected to <c>/wallet/signin</c> by the PWA's
    /// <c>RedirectToSignIn</c> component, and the sign-in screen must render.
    /// </summary>
    /// <remarks>
    /// This test requires no seeded data. It exercises the
    /// <c>[Authorize]</c> + <c>NotAuthorized</c> gate in <c>App.razor</c>
    /// and the base-relative navigation in <c>RedirectToSignIn.razor</c>.
    /// A failure here means the auth gate is broken for all wallet users.
    /// </remarks>
    [Test]
    [Retry(2)]
    public async Task SignedOut_ProtectedRoute_RedirectsToSignIn()
    {
        // Navigate directly to a protected wallet page in unauthenticated state.
        // IndexedDB is empty for a fresh Playwright browser context — no JWT
        // is present, so WalletAuthenticationStateProvider returns
        // NotAuthenticated and the router fires RedirectToSignIn.
        await NavigateToWalletAndWaitForBlazorAsync("/devices");

        // Allow Blazor's client-side routing to settle.
        // WaitForURLAsync with a glob pattern handles both /wallet/signin and
        // /wallet/signin?returnUrl=… (the returnUrl is appended by RedirectToSignIn).
        try
        {
            await Page.WaitForURLAsync(
                $"**{TestConstants.CitizenWalletBase}signin*",
                new() { Timeout = TestConstants.PageLoadTimeout, WaitUntil = WaitUntilState.Load });
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"Expected redirect to /wallet/signin after visiting a protected route " +
                $"while signed out, but the URL is still '{Page.Url}'. " +
                $"The RedirectToSignIn component may not be wired correctly.");
        }

        // Confirm the URL contains the sign-in path segment.
        var actualPath = new Uri(Page.Url).AbsolutePath;
        Assert.That(actualPath, Does.Contain("/wallet/signin"),
            $"URL after sign-out gate redirect should contain '/wallet/signin'. Actual: {actualPath}");

        // Confirm the Blazor sign-in screen rendered (data-testid='signin-screen').
        await SignInPage.WaitForReadyAsync();
        var screenVisible = await SignInPage.Screen.IsVisibleAsync();
        Assert.That(screenVisible, Is.True,
            "The sign-in screen (data-testid='signin-screen') must be visible after the auth-gate redirect.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Password happy-path — requires seeded citizen account
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A citizen with valid password credentials can sign in through the
    /// wallet sign-in screen and is navigated away from <c>/wallet/signin</c>.
    /// </summary>
    /// <remarks>
    /// <b>Marked [Explicit]</b> because it requires a pre-seeded citizen
    /// account in the Docker PostgreSQL database. The council citizen
    /// (<see cref="TestConstants.CouncilTestUsers.CitizenEmail"/>) is created
    /// by the <c>CouncilCredentialFlowTests</c> setup fixture, not a
    /// standalone seed. Run against a stack that has already executed those
    /// setup steps, or supply credentials via the
    /// <c>SORCHA_E2E_COUNCIL_CITIZEN_PASSWORD</c> environment variable.
    /// To run locally: <c>dotnet test --filter "FullyQualifiedName~Password_HappyPath" --explicit</c>
    /// </remarks>
    [Test]
    [Explicit("Requires seeded citizen account — see CouncilTestUsers / CouncilCredentialFlowTests setup.")]
    public async Task Password_HappyPath_SignsInAndLeavesSignInScreen()
    {
        await NavigateToWalletAndWaitForBlazorAsync("/signin");
        await SignInPage.WaitForReadyAsync();

        await SignInPage.SignInWithPasswordAsync(
            TestConstants.CouncilTestUsers.CitizenEmail,
            TestConstants.CouncilTestUsers.CitizenPassword);

        // Wait for the navigation away from /signin.
        try
        {
            await Page.WaitForURLAsync(
                url => !url.Contains("/wallet/signin", StringComparison.OrdinalIgnoreCase),
                new() { Timeout = TestConstants.PageLoadTimeout });
        }
        catch (TimeoutException)
        {
            // If we are still on the sign-in page, surface the error message.
            var errorText = await SignInPage.Error.IsVisibleAsync()
                ? await SignInPage.Error.InnerTextAsync()
                : "(no error element visible)";

            Assert.Fail(
                $"Expected navigation away from /wallet/signin after successful password sign-in, " +
                $"but the URL is still '{Page.Url}'. Error on screen: {errorText}");
        }

        var finalPath = new Uri(Page.Url).AbsolutePath;
        Assert.That(finalPath, Does.Not.Contain("/wallet/signin"),
            $"After successful sign-in the wallet should not stay on /wallet/signin. Actual: {finalPath}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Social return-leg — requires token-mint capability
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The social OAuth return leg: the gateway callback redirects to
    /// <c>/wallet/#token=…&amp;refresh=…&amp;expires_in=…</c>. The
    /// <c>auth-fragment.js</c> script captures the token before Blazor boots,
    /// and <c>OnInitializedAsync</c> in <c>SignIn.razor</c> calls
    /// <c>TryConsumeSocialReturnAsync</c> which writes the token to IndexedDB
    /// and navigates to the wallet Home.
    /// </summary>
    /// <remarks>
    /// <b>Marked [Explicit]</b> because constructing a structurally valid
    /// <c>sorcha:consumer</c> JWT that passes <c>IAccessTokenStore</c>
    /// signature verification requires access to the tenant service's signing
    /// key — there is no public token-mint helper in the test infrastructure.
    /// To run: supply a real token from a prior API login or wire a
    /// <c>GetTokenAsync</c>-style helper (see <c>CouncilCredentialFlowTests</c>).
    /// When the token IS valid, the test verifies the complete fragment-return
    /// path without a UI interaction (the redirect destination carries the token).
    /// </remarks>
    [Test]
    [Explicit("Requires a valid consumer-tier JWT — no token-mint helper available in E2E infra for the citizen audience.")]
    public async Task SocialReturnLeg_StoresTokenAndLeavesSignIn()
    {
        // Placeholder: replace <accessToken>, <refreshToken> with real tokens
        // obtained via e.g. CouncilCredentialFlowTests.GetTokenAsync(...).
        const string accessToken = "REPLACE_ME";
        const string refreshToken = "REPLACE_ME";
        const int expiresIn = 3600;

        // Navigate to the wallet base with the OAuth fragment. auth-fragment.js
        // captures the hash before Blazor boots and replaces the URL state.
        var fragmentUrl =
            $"{TestConstants.UiWebUrl}{TestConstants.CitizenWalletBase}" +
            $"#token={accessToken}&refresh={refreshToken}&expires_in={expiresIn}";

        await Page.GotoAsync(fragmentUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for Blazor WASM to hydrate and process the fragment.
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // The PWA should have consumed the token from the fragment and navigated
        // to the wallet Home — it must NOT remain on /wallet/signin.
        var finalUrl = Page.Url;
        Assert.That(finalUrl, Does.Not.Contain("/wallet/signin"),
            $"After a social return with a valid token the wallet should navigate " +
            $"away from /signin. Actual URL: {finalUrl}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Passkey option visibility — CDP virtual authenticator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// With a CDP virtual WebAuthn authenticator registered, the sign-in screen
    /// should expose the <c>data-testid="signin-passkey"</c> button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The passkey button is conditionally rendered: <c>OnAfterRenderAsync</c>
    /// calls <c>IPasskeyInterop.IsSupportedAsync()</c>, which evaluates
    /// <c>window.PublicKeyCredential !== undefined</c>. Enabling the CDP
    /// virtual authenticator causes the browser to advertise WebAuthn support,
    /// so the button should appear after the next Blazor re-render cycle.
    /// </para>
    /// <para>
    /// The full WebAuthn ceremony (credential creation + assertion against a
    /// server-seeded credential) is <b>NOT</b> tested here — that requires a
    /// pre-registered passkey in the Wallet Service database, which is not
    /// available in the baseline test environment. The full ceremony is deferred
    /// to a future seeded fixture.
    /// </para>
    /// </remarks>
    [Test]
    [Retry(2)]
    public async Task Passkey_VirtualAuthenticator_ShowsPasskeyButton()
    {
        // Enable the CDP virtual WebAuthn authenticator BEFORE navigating so
        // that window.PublicKeyCredential is available from first parse.
        var cdpSession = await Page.Context.NewCDPSessionAsync(Page);

        try
        {
            await cdpSession.SendAsync("WebAuthn.enable");
            await cdpSession.SendAsync("WebAuthn.addVirtualAuthenticator", new Dictionary<string, object>
            {
                ["options"] = new Dictionary<string, object>
                {
                    ["protocol"]          = "ctap2",
                    ["transport"]         = "internal",
                    ["hasResidentKey"]    = true,
                    ["hasUserVerification"] = true,
                    ["isUserVerified"]    = true,
                },
            });
        }
        catch (Exception ex)
        {
            // CDP WebAuthn emulation requires a Chromium-based browser. If the
            // test runner uses Firefox or WebKit, surface as inconclusive.
            Assert.Inconclusive(
                $"CDP WebAuthn.enable failed — likely not running on Chromium. " +
                $"Passkey-button test requires a Chromium browser. Details: {ex.Message}");
            return;
        }

        await SignInPage.GotoAsync();
        await SignInPage.WaitForReadyAsync();

        // IPasskeyInterop.IsSupportedAsync runs in OnAfterRenderAsync (firstRender only),
        // triggering a StateHasChanged. Give Blazor time to complete the re-render.
        await Page.WaitForTimeoutAsync(TestConstants.BlazorHydrationTimeout);

        // The passkey button must be visible now that WebAuthn is available.
        var isVisible = await SignInPage.PasskeyButton.IsVisibleAsync();
        Assert.That(isVisible, Is.True,
            "With a CDP virtual WebAuthn authenticator registered the " +
            "'Sign in with a passkey' button (data-testid='signin-passkey') should be visible.");
    }
}
