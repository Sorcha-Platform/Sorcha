// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Capture;
using Sorcha.Wallet.Pwa.Services.Context;
using Sorcha.Wallet.Pwa.Services.Enrolment;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Sorcha.Wallet.Pwa.Services.Signing;
using Sorcha.Wallet.Pwa.Services.Verification;
using Sorcha.UI.Components.User.Services.Capture;
using Sorcha.UI.Components.User.Services.Signing;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Core.Services;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Wallet.Pwa.Extensions;

/// <summary>DI extensions for the citizen wallet PWA (Feature 114, T065).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the PWA's services as singletons (single-user-per-tab).
    /// Production wiring uses WebCrypto for device keys and IndexedDB for the
    /// credential cache, delegation store, and status list cache — all backed
    /// by <c>indexeddb-bridge.js</c>. The in-memory variants are kept for unit
    /// tests where IJSRuntime isn't available.
    /// </summary>
    public static IServiceCollection AddCitizenWalletServices(
        this IServiceCollection services, string gatewayBaseAddress)
    {
        services.AddSingleton(TimeProvider.System);

        // Phase 1 of the Snackbar retirement — page-scoped inline feedback for
        // actor's-own-action results. Replacement for ISnackbar; see
        // .snackbar-allowlist + scripts/check-no-snackbar.ps1 for the ratchet.
        services.AddSingleton<Sorcha.UI.Core.Services.Feedback.IInlineFeedback,
                              Sorcha.UI.Core.Services.Feedback.InlineFeedback>();
        services.AddSingleton<IPresentationEngine, PresentationEngine>();
        services.AddSingleton<IDeviceKeyService, WebCryptoDeviceKeyService>();
        services.AddSingleton<ICredentialCache, IndexedDbCredentialCache>();
        services.AddSingleton<IDelegationStore, IndexedDbDelegationStore>();
        services.AddSingleton<IStatusListService, IndexedDbStatusListService>();
        services.AddSingleton<ISyncCursorStore, IndexedDbSyncCursorStore>();
        services.AddSingleton<IAccessTokenStore, IndexedDbAccessTokenStore>();
        services.AddSingleton<WalletAuthenticationStateProvider>();
        services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
            sp => sp.GetRequiredService<WalletAuthenticationStateProvider>());
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IDeviceMetaStore, IndexedDbDeviceMetaStore>();
        services.AddSingleton<IEnrolmentService, EnrolmentService>();
        services.AddSingleton<IDelegationRenewalClient, DelegationRenewalClient>();

        // Feature 124 — per-device wallet flags (welcome-takeover dismissal,
        // plus Feature 125 guided-tour dismissal).
        services.AddSingleton<IWalletFlagsStore, IndexedDbWalletFlagsStore>();

        // Feature 141 — theme service so the shell (MainLayout) can drive dark
        // mode from the citizen's resolved preference. IThemeService lives in
        // Sorcha.UI.Core but its registration was web-host-only; the PWA must
        // register it too or MainLayout's @inject fails at startup.
        // UserPreferencesService swallows transport errors and returns defaults,
        // so an unauthenticated citizen (no per-user prefs endpoint) still gets
        // OS-driven light/dark via ThemeService's matchMedia detection.
        services.AddSingleton<IUserPreferencesService>(sp =>
            new UserPreferencesService(
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UserPreferencesService>>()));
        services.AddSingleton<IThemeService>(sp =>
            new ThemeService(
                sp.GetRequiredService<IUserPreferencesService>(),
                sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ThemeService>>()));

        // Feature 125 — PR-A foundation. The signing seam (IUserSigner) and
        // its v1 managed-mode implementation; per-device stores for active
        // org context, per-context persona cache, verification history. The
        // ephemeral verifier identity implementation lands in PR-C (US1)
        // along with the QR/NFC scanner; the interface is already in place
        // so its consumers compile against the contract.
        services.AddSingleton<IUserSigner, ManagedUserSigner>();
        services.AddSingleton<IActiveContextStore, IndexedDbActiveContextStore>();
        services.AddSingleton<IPerContextPersonaCache, IndexedDbPerContextPersonaCache>();
        services.AddSingleton<IVerificationHistoryStore, IndexedDbVerificationHistoryStore>();
        services.AddSingleton<IPresentationLog, IndexedDbPresentationLog>();

        // Feature 125 / PR-C — doorstep verification (US1). EphemeralVerifierIdentity
        // generates a fresh EC P-256 key per session via webcrypto-bridge.js;
        // StubVerifierEngine ships the v1 verify pipeline as a parseable
        // demo offer envelope. Real local-validation lifts
        // VerifiablePresentationValidator out of Sorcha.Verifier in a
        // follow-up extraction PR.
        services.AddSingleton<IEphemeralVerifierIdentityService, EphemeralVerifierIdentityService>();

        // Real validator + adapter (Feature 126, replaces the PR-C stub).
        // VerifiablePresentationValidator was extracted to Sorcha.Verifier.Engine
        // so the wallet runs the same OID4VP pipeline as the desk verifier.
        // StubVerifierEngine stays in the assembly for tests / debug staging
        // but is no longer DI-registered.
        services.AddSingleton<Sorcha.Verifier.Engine.IStatusListCache, Sorcha.Verifier.Engine.StatusListCache>();
        services.AddSingleton<Sorcha.Verifier.Engine.IIssuerKeyResolver, Sorcha.Verifier.Engine.OptOutIssuerKeyResolver>();
        services.AddSingleton<Sorcha.Verifier.Engine.IVerifiablePresentationValidator>(sp =>
            new Sorcha.Verifier.Engine.VerifiablePresentationValidator(
                sp.GetRequiredService<Sorcha.Verifier.Engine.IStatusListCache>(),
                sp.GetRequiredService<Sorcha.Verifier.Engine.IIssuerKeyResolver>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.Verifier.Engine.VerifiablePresentationValidator>>()));
        services.AddSingleton<IVerifierEngine, RealVerifierEngine>();

        // Feature 125 / PR-D — application-from-phone (US2). PortraitCaptureControl
        // routes through IWebCameraService; the file-upload fallback is the v1 path
        // until webcamera-bridge.js + native camera land in a follow-up.
        services.AddSingleton<IWebCameraService, FileFallbackWebCameraService>();
        // Bucket B (review §4): the F125 StubApplicationSubmissionService no-op is NOT registered
        // for production — it faked submission success and is superseded by the F137
        // IApplicationActionClient path (ApplicationInstance.razor). The class + its tests are
        // retained as a test-only seam; it is no longer injectable into the running app.

        // Server-clock observer (T101) — populated by the ServerClockHandler on
        // every outbound HTTP call; read by Pages/Index.razor to surface a
        // clock-skew banner if the device drifts far from the server.
        services.AddSingleton<IServerClockObserver, ServerClockObserver>();
        services.AddTransient<ServerClockHandler>();

        // Sign-out cascade: wipes every per-device IndexedDB store so a
        // signed-out citizen leaves no credentials, personas, history, or
        // welcome/tour flags behind for the next user on the device.
        services.AddSingleton<ILocalDataPurge, IndexedDbLocalDataPurge>();

        // WebAuthn ceremony bridge for the wallet sign-in screen (Feature 138).
        // Behind IPasskeyInterop so AuthService unit tests use FakePasskeyInterop
        // instead of mocking IJSRuntime (brittle — F114 lesson).
        services.AddSingleton<IPasskeyInterop, PasskeyInterop>();

        // Unauthenticated client used ONLY by BearerTokenHandler to refresh — must NOT carry the
        // bearer handler (no recursion).
        services.AddHttpClient("AuthRefresh", c => c.BaseAddress = new Uri(gatewayBaseAddress));
        services.AddTransient(sp => new BearerTokenHandler(
            sp.GetRequiredService<IAccessTokenStore>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("AuthRefresh")));

        // Auth surface: a separate HttpClient that does NOT inject the bearer token
        // (so sign-in requests don't carry stale tokens). Still observes the server
        // clock — sign-in is a great moment to capture initial drift.
        services.AddHttpClient<IAuthService, AuthService>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 126 — enrolment-session redeem client. Anonymous: the
        // session token in the request body is the credential. Still
        // observes the server clock so a clock-skew banner can surface
        // before the citizen confirms.
        services.AddHttpClient<IEnrolSessionRedeemer, EnrolSessionRedeemer>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<ServerClockHandler>();

        // Citizen wallet client: every outbound call automatically carries the
        // wallet's stored bearer token AND records server clock observations.
        services.AddHttpClient<ICitizenWalletClient, CitizenWalletClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 124 — pending-application notice client. Same auth chain as
        // the citizen wallet client (BearerTokenHandler + ServerClockHandler).
        services.AddHttpClient<IPendingApplicationClient, HttpPendingApplicationClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 137 — holder-keys client for HolderKeyRenderer (cross-node submission).
        // Same auth chain so the consumer-tier JWT reaches GET /api/v1/wallet/holder-keys.
        services.AddHttpClient<Sorcha.UI.Core.Services.HolderKeys.IHolderKeyClient,
                               Sorcha.UI.Core.Services.HolderKeys.HolderKeyHttpClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 137 — application action client (cross-node submission). Loads the instance's
        // current action for SorchaFormRenderer and POSTs the completed form to the Blueprint
        // Service /execute endpoint (server-signed via the bearer token). Same auth chain.
        services.AddHttpClient<Sorcha.Wallet.Pwa.Services.Applications.IApplicationActionClient,
                               Sorcha.Wallet.Pwa.Services.Applications.HttpApplicationActionClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 128 — shared has-any-device probe. Drives the wallet PWA
        // pairing-takeover trigger (sub-PR A3) and the Sorcha Web nag-banner
        // trigger (sub-PR B). Registered Singleton so the cached value +
        // Changed event reach every subscriber across the wallet.
        services.AddHttpClient<Sorcha.UI.Core.Services.User.Devices.IHasPairedDeviceProbe,
                               Sorcha.UI.Core.Services.User.Devices.HasPairedDeviceProbe>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 128 US1 — PWA-side short-code redeemer for the PairingTakeover
        // sub-affordance ("Already started on another device?"). Anonymous
        // call: the 6-digit code is the credential for this single endpoint.
        services.AddHttpClient<IPairingShortCodeRedeemer, PairingShortCodeRedeemer>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<ServerClockHandler>();

        // Social sign-in provider list (anonymous endpoint — no bearer token).
        // Still observes the server clock for consistency with other unauthenticated calls.
        services.AddHttpClient<ISocialProvidersClient, SocialProvidersClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<ServerClockHandler>();

        // Feature 125 / PR-B — user context (active org) + memberships client.
        // ManagedUserContext drives /api/auth/switch-org through the bearer-
        // and clock-handler chain so the new JWT is acquired with the
        // currently-stored token. Both surfaces register as singletons so the
        // context-changed event reaches every subscriber across the wallet.
        services.AddHttpClient<IUserOrgMembershipsClient, HttpUserOrgMembershipsClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();
        services.AddHttpClient("UserContextSwitchOrg", c => c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();
        services.AddSingleton<IUserContext>(sp => new ManagedUserContext(
            sp.GetRequiredService<IActiveContextStore>(),
            sp.GetRequiredService<IAccessTokenStore>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("UserContextSwitchOrg"),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ManagedUserContext>>()));

        // Feature 114 / US4 — citizen wallet hub connection. Singleton so the
        // PWA holds a single live SignalR socket across page navigation; the
        // connection authenticates lazily via the access-token store.
        services.AddSingleton(sp => new CitizenWalletHubConnection(
            gatewayBaseAddress,
            sp.GetRequiredService<IAccessTokenStore>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CitizenWalletHubConnection>>()));

        // Feature 118 / Phase A — durable inbox surface. The PWA mirrors the
        // main web app: an HTTP client for /api/me/inbox/* under the bearer-
        // and clock-handler chain, plus a TenantHubConnection singleton for
        // realtime InboxEntryAdded / InboxUnreadCountUpdated nudges.
        services.AddHttpClient<IInboxApiService, InboxApiService>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddHttpMessageHandler<ServerClockHandler>();

        services.AddSingleton(sp => new TenantHubConnection(
            gatewayBaseAddress,
            accessTokenProvider: async () =>
            {
                var record = await sp.GetRequiredService<IAccessTokenStore>().GetAsync();
                return record?.AccessToken;
            },
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TenantHubConnection>>(),
            sp.GetService<IInboxApiService>()));

        return services;
    }
}
