// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.PageObjects.Shared;

namespace Sorcha.UI.E2E.Tests.Infrastructure;

/// <summary>
/// Feature 114 / US4 — base class for E2E tests that target the Citizen Wallet
/// PWA at <c>/wallet/</c> (mounted on the gateway via PathRemovePrefix).
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="AuthenticatedDockerTestBase"/> for two reasons:
/// </para>
/// <list type="number">
///   <item><description>
///     The PWA is served root-relative inside <c>/wallet/</c>, not under
///     <c>/app/</c>, so navigation helpers must rebase paths.
///   </description></item>
///   <item><description>
///     PWA auth state lives in IndexedDB (<c>IAccessTokenStore</c>) under the
///     <c>sorcha:citizen-wallet</c> audience, populated by the in-PWA enrol
///     flow. Playwright's <c>StorageState</c> does not capture IndexedDB, so
///     this base class deliberately does NOT reuse the global main-UI auth
///     state — tests that need an enrolled citizen must drive enrolment in
///     their own setup, or use the demo-mint path which writes directly to
///     IndexedDB through the page UI.
///   </description></item>
/// </list>
/// <para>
/// MudBlazor layout health is asserted by default (the PWA uses MudBlazor).
/// Network-failure assertions are softened to a warning because the PWA
/// background-syncs against <c>/api/v1/wallet/sync</c> immediately on Home
/// load and a 401 there is normal until enrolment completes.
/// </para>
/// </remarks>
public abstract class AuthenticatedCitizenWalletTestBase : DockerTestBase
{
    /// <inheritdoc />
    protected override bool ValidateLayoutHealth => true;

    /// <summary>
    /// The PWA's anonymous-state sync call returns 401 until the user has
    /// enrolled a device. That's expected, not a regression, so the failure
    /// list is downgraded to a warning rather than a hard assert.
    /// </summary>
    protected override bool AssertNoNetworkFailures => false;

    /// <summary>
    /// Navigates to a path inside the citizen-wallet mount. Pass a leading
    /// slash for PWA routes (e.g. <c>"/"</c>, <c>"/enrol"</c>); the wallet
    /// base is prepended automatically.
    /// </summary>
    protected async Task NavigateToWalletAsync(string pwaPath = "/")
    {
        var trimmed = pwaPath.StartsWith('/') ? pwaPath[1..] : pwaPath;
        var url = $"{TestConstants.UiWebUrl}{TestConstants.CitizenWalletBase}{trimmed}";
        await Page.GotoAsync(url);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>
    /// Navigates to the citizen-wallet PWA and waits for Blazor WASM hydration.
    /// </summary>
    protected async Task NavigateToWalletAndWaitForBlazorAsync(string pwaPath = "/")
    {
        await NavigateToWalletAsync(pwaPath);
        await MudBlazorHelpers.WaitForBlazorAsync(Page, TestConstants.PageLoadTimeout);
    }
}
