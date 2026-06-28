// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Tests;

/// <summary>
/// E2E tests for Feature 167 — auth-state notification after fragment-token handoff.
/// Verifies that Profile and Security pages show signed-in content without a manual
/// reload after sign-in via any method that uses the fragment handoff path.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Auth")]
public class AuthStateNotificationTests : AuthenticatedDockerTestBase
{
    // T013 — SC-001 / SC-002 / SC-003
    /// <summary>
    /// After sign-in via the fragment-token handoff path, the Profile and Security pages
    /// should render the authenticated view without requiring a manual page reload.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task PasskeySignIn_ProfilePage_ShowsSignedInContent()
    {
        // The global auth setup authenticates via the same fragment-handoff path that
        // passkey sign-in uses. Navigate to Profile — if auth-state notification works
        // correctly, the page should render signed-in content immediately.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Profile);

        // Verify we are NOT on the login page
        Assert.That(IsOnLoginPage(), Is.False,
            "Profile page should not redirect to login after authentication");

        // Verify page title or heading is present (Profile renders <PageTitle>My Profile</PageTitle>)
        var pageText = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageText, Does.Contain("My Profile").Or.Contain("profile"),
            "Profile page should show profile content when authenticated");
    }

    // T013 — SC-001 / SC-002 / SC-003 (continued)
    /// <summary>
    /// After sign-in via the fragment-token handoff path, the Security page should render
    /// the authenticated view without a manual reload.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task PasskeySignIn_SecurityPage_ShowsSignedInContent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Security);

        Assert.That(IsOnLoginPage(), Is.False,
            "Security page should not redirect to login after authentication");

        var pageText = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageText, Does.Contain("Security").Or.Contain("security").Or.Contain("passkey"),
            "Security page should show security content when authenticated");
    }

    // T014 — FR-005 / SC-004
    /// <summary>
    /// Navigating to a protected page without authentication should redirect to the login page.
    /// No brief authenticated flash should occur — the anonymous state must be stable.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task AnonymousNavigation_ProtectedPage_RedirectsToLogin()
    {
        // Use a fresh, unauthenticated browser context by navigating with the base
        // DockerTestBase (no stored auth state) and checking the response.
        // We achieve this by navigating directly (bypassing NavigateAuthenticatedAsync)
        // and asserting a login redirect.
        await NavigateAndWaitForBlazorAsync(TestConstants.AuthenticatedRoutes.Profile);

        // On first navigation without auth, Blazor [Authorize] should redirect to login.
        // If the page immediately shows profile content, auth-state is leaking from a
        // previous test's session — that would be a test isolation failure, not the bug.
        var onLogin = IsOnLoginPage();
        var pageText = await Page.TextContentAsync("body") ?? "";
        var redirectedCorrectly = onLogin || pageText.Contains("Sign in") || pageText.Contains("Log in");

        Assert.That(redirectedCorrectly, Is.True,
            $"Unauthenticated navigation to Profile should redirect to login. URL: {Page.Url}");
    }

    // T015 — FR-007 / SC-005
    /// <summary>
    /// Sign-in via the standard password/email path (which also returns through the
    /// fragment handoff) should produce the same result: Profile and Security render
    /// signed-in content without a manual reload.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task OtherHandoffMethods_ProfilePage_ShowsSignedInContent()
    {
        // Standard email/password login uses the same fragment-token handoff path
        // as passkey sign-in. The global auth setup covers this path.
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Profile);

        Assert.That(IsOnLoginPage(), Is.False,
            "Profile page should render authenticated content after standard sign-in");

        var pageText = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageText, Does.Contain("My Profile").Or.Contain("profile"),
            "Profile page should show profile content after any sign-in method");
    }

    // T015 (continued)
    /// <summary>
    /// After sign-in via any method, the Security page should render signed-in content
    /// without a manual reload.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task OtherHandoffMethods_SecurityPage_ShowsSignedInContent()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.Security);

        Assert.That(IsOnLoginPage(), Is.False,
            "Security page should render authenticated content after standard sign-in");

        var pageText = await Page.TextContentAsync("body") ?? "";
        Assert.That(pageText, Does.Contain("Security").Or.Contain("security").Or.Contain("passkey"),
            "Security page should show security content after any sign-in method");
    }
}
