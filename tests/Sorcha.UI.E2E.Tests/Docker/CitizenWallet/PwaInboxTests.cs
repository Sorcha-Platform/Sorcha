// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Feature 118 / Phase A — verifies the durable inbox surface in the Citizen
/// Wallet PWA wires up correctly. The bell + drawer relocate to the shared
/// <c>Sorcha.UI.Components.User</c> library and mount in the PWA's
/// <c>MainLayout.razor</c>.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous-state coverage only. The PWA holds its bearer token in IndexedDB
/// (<see cref="Sorcha.Wallet.Pwa.Services.IAccessTokenStore"/>), which
/// Playwright's <c>StorageState</c> does not capture — so an automated
/// citizen-authenticated test would require driving the full enrol flow or
/// stuffing the IndexedDB record directly via JS interop. Both are deferred to
/// the broader Citizen Wallet test-fixture work tracked in issue #700.
/// </para>
/// <para>
/// The realtime sync path — server issues credential → bell badge increments
/// in &lt;3s — is exercised by the manual UAT at Phase A5 of the Snackbar
/// retirement plan. The placeholder test <see cref="InboxBell_BadgeIncrementsOnServerIssuance"/>
/// documents the hook for the eventual automated version.
/// </para>
/// </remarks>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
[Category("Inbox")]
public class PwaInboxTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    [Test]
    [Retry(2)]
    public async Task InboxBell_RendersInPwaAppBar()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // The bell button must be present in the app bar regardless of auth
        // state — it's a layout fixture, not a feature gate. Anonymous
        // visitors see the bell at zero so the surface is discoverable from
        // the first render.
        await Assertions.Expect(Wallet.TopBarInboxButton).ToBeVisibleAsync();
    }

    [Test]
    [Retry(2)]
    public async Task InboxBell_HasZeroBadge_WhenAnonymous()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // The badge is conditional — only rendered when _inboxUnreadCount > 0.
        // Anonymous state: the layout skips the cold-load + hub start, so the
        // badge element should not be in the DOM.
        var badgeCount = await Wallet.TopBarInboxBadge.CountAsync();
        Assert.That(badgeCount, Is.EqualTo(0),
            "Anonymous PWA visit should not render an unread badge — " +
            "InitialiseInboxAsync short-circuits when IAccessTokenStore.GetAsync() returns null.");
    }

    [Test]
    [Retry(2)]
    public async Task InboxBell_Click_OpensDrawer()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        await Wallet.TopBarInboxButton.ClickAsync();

        // The drawer's "Inbox" header text appears when MudDrawer.Open is true.
        // Using a text-anchored locator keeps the test independent of the
        // drawer's class structure (Temporary variant injects a backdrop +
        // animation classes that vary by MudBlazor minor version).
        var drawerHeader = Page.Locator(".mud-drawer:has-text('Inbox')").First;
        await Assertions.Expect(drawerHeader).ToBeVisibleAsync(
            new() { Timeout = 3000 });
    }

    [Test]
    [Retry(2)]
    public async Task InboxDrawer_ShowsEmptyState_WhenAnonymousAndNoEntries()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();
        await Wallet.TopBarInboxButton.ClickAsync();

        // Anonymous citizens hit /api/me/inbox without a bearer token; the
        // gateway returns 401 → InboxApiService.ListAsync logs + returns null
        // → InboxPanel's _entries stays empty → empty-state MudAlert renders.
        // This is the correct fallback shape — the panel never crashes a
        // pre-enrolment session.
        var emptyState = Page.Locator(".mud-alert:has-text('Your inbox is empty')").First;
        await Assertions.Expect(emptyState).ToBeVisibleAsync(
            new() { Timeout = 3000 });
    }

    /// <summary>
    /// Placeholder for the realtime sync test. Asserts that a server-issued
    /// credential lands in the PWA inbox panel within 3 seconds via the
    /// <c>TenantHub.OnInboxEntryAdded</c> path. Requires an authenticated
    /// citizen fixture (IndexedDB token plus a server-side credential-
    /// issuance probe), so currently runs as part of the Phase A5 manual UAT.
    /// </summary>
    [Test]
    [Ignore("Phase A5 / issue #700 — needs an authenticated-citizen IndexedDB fixture. " +
            "Manual UAT covers this path until the fixture lands.")]
    public Task InboxBell_BadgeIncrementsOnServerIssuance()
    {
        // Outline of the eventual automated path:
        //   1. Provision a citizen via /api/auth/register (or a test-only
        //      seeding endpoint) and capture { platformUserId, accessToken }.
        //   2. Page.EvaluateAsync — write { AccessToken, ExpiresAt, Email }
        //      into the indexeddb-bridge under key 'sorcha:access-token'.
        //   3. Reload the PWA; InitialiseInboxAsync now picks up the token
        //      and starts TenantHub.
        //   4. Trigger /api/test/inbox-write (or issue a credential via
        //      WalletInboxWriter) for { platformUserId } → expect a single
        //      InboxEntryAdded.
        //   5. Wait <=3s for TopBarInboxBadge to render with content "1".
        //   6. Open the drawer, assert the entry's title is visible.
        return Task.CompletedTask;
    }
}
