// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Tests.Wallets;

/// <summary>
/// E2E tests for wallet creation wizard behavior (Feature 157 US2).
/// Tests verify the onboarding defaults (24-word, pre-filled name) versus standalone defaults.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("Wallet")]
public class CreateWalletTests : AuthenticatedDockerTestBase
{
    /// <summary>
    /// Onboarding path: navigating to wallet/create from Home.razor onboarding includes
    /// words=24 and a default name in the URL query string.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T010 (Home.razor wallet URL with words=24)")]
    public async Task OnboardingWalletUrl_ContainsWords24AndDefaultName()
    {
        // Navigate to the dashboard as a first-run user (no wallets)
        await NavigateAuthenticatedAsync("/");
        await Page.WaitForLoadStateAsync();

        // Find and click the wallet creation button
        var createButton = Page.Locator("[data-testid='wallet-create-button'], a[href*='wallets/create']");
        await createButton.WaitForAsync(new() { Timeout = 5000 });

        var href = await createButton.GetAttributeAsync("href");

        // The onboarding entry URL must include words=24 (Feature 157 US2, FR-006)
        Assert.That(href, Does.Contain("words=24"), "Onboarding wallet URL should contain words=24");
        Assert.That(href, Does.Contain("name="), "Onboarding wallet URL should contain a default name");
        Assert.That(href, Does.Contain("wizard=true"), "Onboarding wallet URL should retain wizard=true");
    }

    /// <summary>
    /// Onboarding path: the wallet wizard word-count selector defaults to 24.
    /// </summary>
    [Test]
    [Ignore("Pending implementation of T010 (wallet wizard 24-word default via query param)")]
    public async Task OnboardingWalletWizard_WordCountDefaultsTo24()
    {
        await NavigateAuthenticatedAsync("/wallets/create?wizard=true&words=24&name=Test+User");
        await Page.WaitForLoadStateAsync();

        // The word-count selector or radio should show 24 as selected
        var selector = Page.Locator("[data-testid='word-count-selector'], [name='wordCount']");
        var value = await selector.InputValueAsync();

        Assert.That(value, Is.EqualTo("24"), "Word count should default to 24 in onboarding context");
    }

    /// <summary>
    /// Onboarding path: user overrides word count and name; back navigation preserves the override.
    /// </summary>
    [Test]
    [Ignore("Pending implementation: back-navigation preserves user overrides in wizard")]
    public async Task OnboardingWalletWizard_UserOverridesArePreservedOnBackNavigation()
    {
        await NavigateAuthenticatedAsync("/wallets/create?wizard=true&words=24&name=Test+User");
        await Page.WaitForLoadStateAsync();

        // Override word count to 12 (user preference)
        var wordCountInput = Page.Locator("[data-testid='word-count-selector'], [name='wordCount']");
        await wordCountInput.SelectOptionAsync("12");

        // Override wallet name
        var nameInput = Page.Locator("[data-testid='wallet-name-input'], [name='walletName']");
        await nameInput.FillAsync("My Custom Wallet");

        // Navigate back and forward
        await Page.GoBackAsync();
        await Page.GoForwardAsync();
        await Page.WaitForLoadStateAsync();

        var wordCountAfter = await wordCountInput.InputValueAsync();
        var nameAfter = await nameInput.InputValueAsync();

        Assert.That(wordCountAfter, Is.EqualTo("12"), "Word count override should survive back-navigation");
        Assert.That(nameAfter, Is.EqualTo("My Custom Wallet"), "Name override should survive back-navigation");
    }

    /// <summary>
    /// Standalone path: navigating directly to wallets/create (no query string) uses 12-word default
    /// and empty name — confirming standalone behavior is unchanged (FR-009, SC-005).
    /// </summary>
    [Test]
    [Ignore("Pending manual verification; standalone flow should be unchanged from pre-157")]
    public async Task StandaloneWalletCreation_NoQueryString_DefaultsTo12WordsAndEmptyName()
    {
        await NavigateAuthenticatedAsync("/wallets/create");
        await Page.WaitForLoadStateAsync();

        var wordCountInput = Page.Locator("[data-testid='word-count-selector'], [name='wordCount']");
        var value = await wordCountInput.InputValueAsync();

        Assert.That(value, Is.EqualTo("12"), "Standalone wallet creation should default to 12 words");

        var nameInput = Page.Locator("[data-testid='wallet-name-input'], [name='walletName']");
        var nameValue = await nameInput.InputValueAsync();
        Assert.That(nameValue, Is.Empty.Or.EqualTo(string.Empty), "Standalone wallet creation should have empty default name");
    }
}
