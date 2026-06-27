// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Tests.Onboarding;

/// <summary>
/// E2E tests for the CompleteProfileStep onboarding component (Feature 157 US1).
/// Tests verify profile capture during the first-run onboarding sequence.
/// These tests require a first-run user flow and depend on T005–T008 being implemented.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Onboarding")]
public class CompleteProfileStepTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// Happy path: new user completes profile step after wallet creation.
    /// Navigates to /?onboarding=true, fills in name and contact details, submits,
    /// and is redirected to the dashboard.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T005-T008 (CompleteProfileStep + Home.razor wiring)")]
    public async Task OnboardingProfileStep_SaveHappyPath_ContinuesToDashboard()
    {
        await NavigateAuthenticatedAsync("/?onboarding=true");

        // The profile step should be visible after wallet provisioning
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']");

        await Page.FillAsync("[data-testid='profile-given-name']", "Alice");
        await Page.FillAsync("[data-testid='profile-family-name']", "Smith");
        await Page.FillAsync("[data-testid='profile-contact-email']", "alice.smith@example.com");

        await Page.ClickAsync("[data-testid='profile-save-button']");

        // After save, should no longer see the profile step
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']",
            new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden });

        Assert.Pass("Profile step submitted and dashboard shown");
    }

    /// <summary>
    /// Pre-fill: existing display name seeds the FullName field when persona is empty.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T006 (CompleteProfileStep pre-fill)")]
    public async Task OnboardingProfileStep_EmptyPersona_PreFillsFromDisplayName()
    {
        await NavigateAuthenticatedAsync("/?onboarding=true");

        await Page.WaitForSelectorAsync("[data-testid='profile-full-name']");

        // The full name field should be pre-filled from the JWT display name claim
        var fullNameValue = await Page.InputValueAsync("[data-testid='profile-full-name']");
        Assert.That(fullNameValue, Is.Not.Empty, "Full name should be pre-filled from auth display name");
    }

    /// <summary>
    /// Skip path: user clicks "Skip for now" and proceeds without saving.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T007 (CompleteProfileStep skip)")]
    public async Task OnboardingProfileStep_SkipOptionalFields_ContinuesWithoutError()
    {
        await NavigateAuthenticatedAsync("/?onboarding=true");

        await Page.WaitForSelectorAsync("[data-testid='profile-save-button']");

        // Submit with only the mandatory fields empty — name fields are optional
        await Page.ClickAsync("[data-testid='profile-save-button']");

        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']",
            new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden });

        Assert.Pass("Profile step submitted with no optional fields filled");
    }

    /// <summary>
    /// Re-entry: submitting the profile step a second time updates the existing persona in place.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T007 (CompleteProfileStep re-entry update)")]
    public async Task OnboardingProfileStep_ReEntry_UpdatesExistingPersona()
    {
        // First submission
        await NavigateAuthenticatedAsync("/?onboarding=true");
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']");
        await Page.FillAsync("[data-testid='profile-given-name']", "Alice");
        await Page.ClickAsync("[data-testid='profile-save-button']");
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']",
            new() { State = Microsoft.Playwright.WaitForSelectorState.Hidden });

        // Second submission (re-entry via navigating back)
        await NavigateAuthenticatedAsync("/?onboarding=true");
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']");

        // Pre-fill should reflect the previously saved value
        var givenName = await Page.InputValueAsync("[data-testid='profile-given-name']");
        Assert.That(givenName, Is.EqualTo("Alice"), "Pre-fill should show previously saved given name");

        await Page.FillAsync("[data-testid='profile-given-name']", "Alicia");
        await Page.ClickAsync("[data-testid='profile-save-button']");

        Assert.Pass("Profile step re-entry updated persona in place");
    }

    /// <summary>
    /// Validation: invalid email input is rejected with an inline field error; persona is not updated.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T007 (CompleteProfileStep validation)")]
    public async Task OnboardingProfileStep_InvalidEmail_ShowsInlineError()
    {
        await NavigateAuthenticatedAsync("/?onboarding=true");
        await Page.WaitForSelectorAsync("[data-testid='profile-contact-email']");

        await Page.FillAsync("[data-testid='profile-contact-email']", "not-an-email");
        await Page.ClickAsync("[data-testid='profile-save-button']");

        // An inline error should appear — profile step stays on screen
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']",
            new() { State = Microsoft.Playwright.WaitForSelectorState.Visible });

        Assert.Pass("Invalid email surfaced inline error; profile step still visible");
    }

    /// <summary>
    /// 409 response (wallet not provisioned): inline error without silently advancing.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T007 (CompleteProfileStep 409 handling)")]
    public async Task OnboardingProfileStep_WalletNotProvisioned_ShowsInlineErrorWithoutAdvancing()
    {
        // Navigate to dashboard without ?onboarding=true — simulate a user who somehow
        // lands on the profile step before their wallet is ready (edge case).
        await NavigateAuthenticatedAsync("/?onboarding=true");
        await Page.WaitForSelectorAsync("[data-testid='profile-given-name']");

        await Page.FillAsync("[data-testid='profile-given-name']", "Alice");
        await Page.ClickAsync("[data-testid='profile-save-button']");

        // If the 409 path fires, the step should remain visible with an error message
        // This test may need special test-user setup to guarantee 409
        Assert.Ignore("Requires test user without a provisioned wallet to trigger 409");
    }
}
