// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

/// <summary>
/// Feature 114 / US4 — page object for the Citizen Wallet PWA Home
/// (<c>/wallet/</c>). Locators bind to the <c>data-testid</c> attributes
/// added in <c>Sorcha.Citizen.Wallet/Pages/Index.razor</c> so structural
/// changes to MudBlazor markup do not break tests.
/// </summary>
public class CitizenWalletPage
{
    private readonly IPage _page;

    public CitizenWalletPage(IPage page)
    {
        _page = page;
    }

    /// <summary>Home empty-state alert ("No credentials yet…"). Visible iff cache is empty.</summary>
    public ILocator EmptyState => _page.Locator("[data-testid='home-empty-state']");

    /// <summary>"Enrol this device" button — shown alongside the empty state.</summary>
    public ILocator EnrolDeviceButton => _page.Locator("[data-testid='enrol-device-button']");

    /// <summary>The credentials list container — visible iff at least one credential is cached.</summary>
    public ILocator CredentialsList => _page.Locator("[data-testid='credentials-list']");

    /// <summary>All rendered credential cards.</summary>
    public ILocator CredentialCards => _page.Locator("[data-testid^='credential-card-']");

    /// <summary>The transient sync banner (success / warning / error after a sync).</summary>
    public ILocator SyncBanner => _page.Locator("[data-testid='sync-banner']");

    /// <summary>"Sync now" button on Home.</summary>
    public ILocator SyncNowButton => _page.Locator("button:has-text('Sync now')");

    /// <summary>"Load demo credential" button on Home (US4 dev-seed path).</summary>
    public ILocator LoadDemoButton => _page.Locator("button:has-text('Load demo credential')");

    /// <summary>"Present a credential" button on Home.</summary>
    public ILocator PresentButton => _page.Locator("button:has-text('Present a credential')");

    // ─────────────────────────────────────────────────────────────────────────
    // Nav-bar locators (covered by CitizenWalletNavigationTests). Filed
    // against issue #700 — every PWA nav element gets a stable data-testid
    // and a click+URL assertion so the leading-slash NavigateTo regression
    // (PR #698) cannot reappear silently.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Top-bar Present icon (MainLayout AppBar).</summary>
    public ILocator TopBarPresentButton => _page.Locator("[data-testid='topbar-present-button']");

    /// <summary>Footer Home nav button.</summary>
    public ILocator FooterNavHome => _page.Locator("[data-testid='footer-nav-home']");

    /// <summary>Footer Devices nav button.</summary>
    public ILocator FooterNavDevices => _page.Locator("[data-testid='footer-nav-devices']");

    /// <summary>Footer Activity nav button.</summary>
    public ILocator FooterNavActivity => _page.Locator("[data-testid='footer-nav-activity']");

    /// <summary>Footer Settings nav button.</summary>
    public ILocator FooterNavSettings => _page.Locator("[data-testid='footer-nav-settings']");

    /// <summary>Home "Present a credential" button (data-testid'd variant).</summary>
    public ILocator HomePresentButton => _page.Locator("[data-testid='home-present-button']");

    /// <summary>Enrol-page "Open Settings" link (Welcome step, signed-out branch).</summary>
    public ILocator EnrolOpenSettingsLink => _page.Locator("[data-testid='enrol-open-settings-link']");

    /// <summary>Enrol-page "Open wallet" button (Done step).</summary>
    public ILocator EnrolDoneOpenWalletButton => _page.Locator("[data-testid='enrol-done-open-wallet-button']");

    /// <summary>CredentialDetail "Back" button.</summary>
    public ILocator CredentialDetailBackButton => _page.Locator("[data-testid='credential-detail-back-button']");

    /// <summary>CredentialDetail "Present this credential" button.</summary>
    public ILocator CredentialDetailPresentButton => _page.Locator("[data-testid='credential-detail-present-button']");

    /// <summary>
    /// Returns the locator for a specific credential card by id. The id must
    /// match what the PWA stores in <c>CachedCredential.Id</c> (Guid).
    /// </summary>
    public ILocator CredentialCard(Guid credentialId) =>
        _page.Locator($"[data-testid='credential-card-{credentialId}']");

    /// <summary>
    /// Waits for any credential card to appear within <paramref name="timeout"/>.
    /// Use this when the test does not know the exact id ahead of time
    /// (e.g. demo-mint flow generates the id server-side).
    /// </summary>
    public async Task WaitForAnyCredentialAsync(TimeSpan timeout) =>
        await CredentialCards.First.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = (float)timeout.TotalMilliseconds,
        });

    /// <summary>
    /// Waits for the credential card with the given id to appear within
    /// <paramref name="timeout"/>. Used by the push-pipeline test to assert
    /// that <c>CredentialAvailable</c> + <c>/sync</c> deliver a known credential.
    /// </summary>
    public async Task WaitForCredentialAsync(Guid credentialId, TimeSpan timeout) =>
        await CredentialCard(credentialId).WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = (float)timeout.TotalMilliseconds,
        });

    /// <summary>
    /// Waits for the empty state OR the credentials list to render — whichever
    /// applies. Confirms the page got past its initial loading spinner.
    /// </summary>
    public async Task WaitForReadyAsync(TimeSpan? timeout = null)
    {
        var ms = (float)(timeout?.TotalMilliseconds ?? TestConstants.PageLoadTimeout);
        await _page.Locator(
                "[data-testid='home-empty-state'], [data-testid='credentials-list']")
            .First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = ms });
    }

    /// <summary>Returns the number of credential cards currently rendered.</summary>
    public Task<int> CredentialCountAsync() => CredentialCards.CountAsync();
}
