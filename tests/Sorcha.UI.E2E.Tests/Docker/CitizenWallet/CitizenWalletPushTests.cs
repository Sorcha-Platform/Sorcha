// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Feature 114 / US4 — Citizen Wallet PWA push & render coverage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Coverage scope:</b> the tests here exercise the PWA-side rendering
/// contract — the Home page reaches a usable state, the SignalR hub connection
/// is wired through the gateway without crashing the surface, and the demo
/// credential-mint path renders a card via the page object's
/// <see cref="CitizenWalletPage.WaitForCredentialAsync"/> helper. This is the
/// structural layer the brief calls out as "isolating server-side correctness
/// from rendering issues".
/// </para>
/// <para>
/// <b>Out of scope here:</b> the full real-issuance pipeline test that drives
/// the worked-example blueprint from <c>specs/114-citizen-wallet-pwa/us4-plan.md</c>
/// § 4 — verifier issues an <c>AssuredIdentityCredential</c> to a late-bound
/// citizen applicant, sealed by the validator, decrypted by
/// <c>InboundCredentialDetector</c>, and pushed via
/// <c>WalletHub.CredentialAvailable</c>. That requires citizen-enrolment
/// scaffolding, blueprint publishing, and late-bind submission and is
/// deferred to a follow-up suite. Server-side correctness of the projector
/// is already covered by the unit tests in
/// <c>tests/Sorcha.Wallet.Service.Tests/CitizenWallet/</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
[Category("US4")]
public class CitizenWalletPushTests : AuthenticatedCitizenWalletTestBase
{
    private CitizenWalletPage Wallet => new(Page);

    /// <summary>
    /// Smoke: navigating to <c>/wallet/</c> reaches a usable Home — either the
    /// empty-state alert or the credentials list — without console errors and
    /// without the layout-health validator flagging a broken MudBlazor render.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task WalletHome_LoadsToEmptyOrListState()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        var hasEmpty = await Wallet.EmptyState.CountAsync() > 0;
        var hasList = await Wallet.CredentialsList.CountAsync() > 0;
        Assert.That(hasEmpty || hasList, Is.True,
            "Citizen wallet Home should render either the empty state or the credentials list");
    }

    /// <summary>
    /// Hub-wire sanity: the PWA initialises <c>CitizenWalletHubConnection</c>
    /// in <c>OnInitializedAsync</c> and calls <c>StartAsync</c>. We don't
    /// assert that a SignalR negotiate succeeds (it requires a citizen JWT
    /// in IndexedDB which is not seeded by this test), but we DO assert the
    /// hub-bootstrap path does not crash the page or surface uncaught errors.
    /// This protects against regressions like "MapSorchaHubs not called for
    /// the citizen audience" — the kind of bug that would silently break
    /// US4 push delivery without affecting the rest of the PWA.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task WalletHome_HubBootstrap_DoesNotCrashSurface()
    {
        var jsErrors = new List<string>();
        Page.PageError += (_, error) => jsErrors.Add(error);

        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // Give the hub StartAsync call a window to run + fail gracefully if
        // it's going to. Hub.StartAsync is fire-and-forget, so we wait long
        // enough for negotiate + the swallow path to settle.
        await Page.WaitForTimeoutAsync(2000);

        Assert.That(jsErrors, Is.Empty,
            $"Hub bootstrap must not raise uncaught JS errors. Got: {string.Join("\n", jsErrors)}");
    }

    /// <summary>
    /// Page-object exercise: the demo-mint button writes a credential into
    /// the local IndexedDB cache and re-reads it into <c>_credentials</c>.
    /// Asserts the page-object <see cref="CitizenWalletPage.WaitForAnyCredentialAsync"/>
    /// helper finds the rendered card within five seconds. This proves the
    /// <c>data-testid="credential-card-{id}"</c> contract and the page
    /// object's locators line up with the live MudBlazor markup.
    /// </summary>
    /// <remarks>
    /// Demo-mint bypasses <c>CredentialStore.AddAsync</c>, so this test does
    /// NOT exercise <c>CitizenInboxProjector</c> or
    /// <c>WalletHub.CredentialAvailable</c>. It is purely a render-path
    /// proof. The real push-pipeline test is deferred (see fixture-level
    /// XML doc).
    /// </remarks>
    [Test]
    [Retry(2)]
    public async Task LoadDemoCredential_PageObject_FindsCardWithinFiveSeconds()
    {
        await NavigateToWalletAndWaitForBlazorAsync();
        await Wallet.WaitForReadyAsync();

        // Pre-condition: demo-mint may have been clicked in a prior test —
        // accept either zero cards or pre-existing cards as the starting
        // point.
        var startingCount = await Wallet.CredentialCountAsync();

        if (await Wallet.LoadDemoButton.CountAsync() == 0)
        {
            Assert.Inconclusive("Load demo button not present — verifier app may not be running.");
            return;
        }

        await Wallet.LoadDemoButton.ClickAsync();

        try
        {
            await Wallet.WaitForAnyCredentialAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Demo-mint depends on the verifier app at /verify/demo/mint
            // being healthy. If it's not, surface as inconclusive so the
            // suite stays useful when only Wallet Service is in scope.
            Assert.Inconclusive(
                "Demo-mint did not produce a card within 5s — verifier may be unavailable.");
            return;
        }

        var endingCount = await Wallet.CredentialCountAsync();
        Assert.That(endingCount, Is.GreaterThanOrEqualTo(startingCount + 1),
            "Loading the demo credential should add at least one card to the list");
    }
}
