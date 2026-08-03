// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.Blueprint.Schemas.Client;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Sorcha.UI.Core.Services.Http;
using Sorcha.UI.Core.Services.User.Enrolment;
using Sorcha.UI.Core.Services.User.Presentation;
using Sorcha.UI.Web.Client;
using Sorcha.UI.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// AuthorizeView role-mismatch evaluations log at Info per render — every
// admin/designer nav block on MainLayout produces a "Authorization failed"
// line for every non-matching role. For a Consumer that's ~100 lines per
// page load. Silence the category; legitimate auth failures still surface
// as HTTP 401s in the network log.
builder.Logging.AddFilter(
    "Microsoft.AspNetCore.Authorization.DefaultAuthorizationService",
    LogLevel.Warning);

// Register root components for standalone WASM
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register core services (authentication, encryption, configuration) with base address
builder.Services.AddCoreServices(builder.HostEnvironment.BaseAddress);

// Feature 163/164: shared user-facing component library seams + live HAIP verify transport.
// The web client acts as a relying-party verifier. Pass the gateway base address so the HAIP
// verifier client can resolve relative URIs, and attach the web client's
// AuthenticatedHttpMessageHandler so the verify request carries the signed-in user's bearer token
// — the verify endpoints (/api/v1/verifier/requests) require an authenticated caller (Feature 164).
builder.Services.AddSorchaUserComponents(
    builder.Configuration,
    haipBaseUrl: builder.HostEnvironment.BaseAddress,
    configureHaipClient: b => b.AddHttpMessageHandler<AuthenticatedHttpMessageHandler>());

// Override the NotConfigured stub transport with the live HAIP transport (Feature 164). Without
// this override the web client always reported "Verification is not yet configured here."
builder.Services.AddScoped<IVerifierIdentityProvider, WebVerifierIdentityProvider>();
builder.Services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

// Feature 186 (#1163) — route inbox detailHrefs to this host's My Applications view. Registered
// with AddScoped (not TryAdd) so it WINS over the TryAddScoped default in Components.User, which
// refuses /api/* hrefs and left every decision notice a dead row on the web.
builder.Services.AddScoped<Sorcha.UI.Components.User.Services.Shared.IInboxDetailRouter,
    Sorcha.UI.Web.Client.Services.WebInboxDetailRouter>();

// Register authorization
builder.Services.AddAuthorizationCore();

// Add MudBlazor services
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.ShowTransitionDuration = 300;
    config.SnackbarConfiguration.HideTransitionDuration = 300;
});

// Add local storage for WASM pages (blueprints, schemas, user preferences)
builder.Services.AddBlazoredLocalStorage();

// Add blueprint serialization service for export/import
builder.Services.AddScoped<BlueprintSerializationService>();

// Add blueprint layout service for visual diagram rendering
builder.Services.AddScoped<BlueprintLayoutService>();

// Add passkey interop service for WebAuthn browser API calls
builder.Services.AddScoped<PasskeyInteropService>();

// Add navigation state service for passing objects between page navigations (Feature 091)
builder.Services.AddScoped<NavigationStateService>();

// Add Designer shell shared context (Feature 109 — AI Designer unified shell)
builder.Services.AddScoped<Sorcha.UI.Core.Services.Designer.DesignerContext>();

// Add schema library service with caching
// All schemas (local defaults, external, remote) are served through the unified
// schema index API — the client only needs the Blueprint Service repository.
builder.Services.AddScoped<ISchemaCacheService, LocalStorageSchemaCacheService>();
builder.Services.AddScoped<SchemaLibraryService>(sp =>
{
    var cacheService = sp.GetRequiredService<ISchemaCacheService>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var schemaLibrary = new SchemaLibraryService(cacheService, loggerFactory);

    var httpClient = sp.GetRequiredService<HttpClient>();
    schemaLibrary.AddRepository(new BlueprintServiceRepository(httpClient));

    return schemaLibrary;
});

// Feature 126 — council enrolment gate services. ITierProbeService hits the
// API gateway directly via a typed HttpClient; IEnrolPairingSignal layers
// SignalR (via TenantHubConnection) with a 3-s /me/devices poll as fallback.
//
// ITierProbeService is deliberately left ANONYMOUS: its whole job is to classify the visitor, and a
// 401 from /whoami IS the answer "cold-start visitor, tier 3". Attaching the authenticated handler
// would turn that expected 401 into a redirect-to-login and break the cold-start gate.
builder.Services.AddHttpClient<ITierProbeService, HttpTierProbeService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// Everything below calls an endpoint scoped to the SIGNED-IN citizen, so each typed client must
// carry their bearer token. The host's ambient HttpClient cannot: it is the plain client
// AuthenticationService itself uses, so it can't depend on the auth handler without a circular
// dependency. A client registered without the handler here silently sends anonymous requests and
// gets a 401 — which is exactly how "Couldn't generate a pairing code." reached a signed-in citizen.
builder.Services.AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Features 126/128 — mints the enrol-session token (QR) and the 6-digit pairing short code for
// WalletPairingSurface and PairingHandoffSurface (/setup/add-device).
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Pairing.IPairingClient,
                               Sorcha.UI.Core.Services.User.Pairing.PairingClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Feature 127 — the presentation-gate stack: BlueprintHub connection, IPresentationSignal, and the
// SorchaWalletGateTransport that PresentationRequestCard resolves for a SorchaWallet gate. Until
// now only the sample council portal registered this, so the web app had no transport for a
// SorchaWallet-sourced gate and fell through to HAIP.
//
// The typed HTTP clients are deliberately NOT wrapped in AuthenticatedHttpMessageHandler:
// /api/presentations/{id}/status and /disclosed-claims are AllowAnonymous by design,
// authenticated instead by the high-entropy request id and the single-use ClaimsFetchToken.
//
// The HUB is different. AddSorchaPresentationGate defaults its accessTokenProvider to null for the
// council-page case (a third-party portal has no user session), but /hubs/blueprint DOES require
// auth — so on this host, which always has a session, omitting the provider makes negotiate 401
// and the hub never connects. Observed live on n1: the gate silently degraded to the 3s poll
// fallback on every presentation.
builder.Services.AddSorchaPresentationGate(
    builder.HostEnvironment.BaseAddress,
    accessTokenProvider: sp =>
    {
        var authService = sp.GetRequiredService<IAuthenticationService>();
        var configService = sp.GetRequiredService<IConfigurationService>();
        return async () =>
        {
            var profileName = await configService.GetActiveProfileNameAsync();
            return await authService.GetAccessTokenAsync(profileName);
        };
    });

// #1330 — local (same-device) completion of a SorchaWallet presentation gate. Registered ONLY
// on hosts with a signed-in citizen session: every call (export, sign-kb, direct_post) is
// consumer-tier, so the client MUST carry the bearer (see the ambient-HttpClient warning above).
// PresentationRequestCard resolves this via GetService — hosts without it (the council sample
// portal) fall back to the QR route.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Presentation.ISorchaWalletLocalPresenter,
                               Sorcha.UI.Core.Services.User.Presentation.SorchaWalletLocalPresenter>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Feature 181 US3 (T037) — platform-admin trusted-list snapshot management. The endpoints are
// admin + platform-audience gated, so the client must carry the admin's bearer token.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.Admin.ITrustedListAdminService,
    Sorcha.UI.Core.Services.Admin.TrustedListAdminService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Feature 181 US5 (T050) — org-admin X.509 certificate lifecycle management.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.Admin.IOrgCertificateAdminService,
    Sorcha.UI.Core.Services.Admin.OrgCertificateAdminService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Feature 128 — shared has-any-device probe. Drives the PairingNagBanner
// on every MainLayout render for signed-in citizens with zero paired
// devices (FR-024). The same probe is also registered in the PWA
// (Sorcha.Wallet.Pwa) where it drives the takeover trigger (sub-PR A3).
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Devices.IHasPairedDeviceProbe,
                               Sorcha.UI.Core.Services.User.Devices.HasPairedDeviceProbe>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Issue #1311 — the My Devices page's list + revoke. Registered auth-wrapped because both endpoints
// are authenticated; the page previously used the ambient HttpClient (no Authorization header) and
// so 401'd on every request, reporting it to the citizen as a connection problem.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Devices.IMyDevicesClient,
                               Sorcha.UI.Core.Services.User.Devices.MyDevicesClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Issue #1311 — Feature 085 file-chunk upload. /api/file-chunks carries .RequireAuthorization(),
// and FileReferenceField previously POSTed to it on the ambient HttpClient, so every chunk 401'd.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Files.IFileChunkUploadClient,
                               Sorcha.UI.Core.Services.User.Files.FileChunkUploadClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Issue #1311 — the F079 verification-bundle download (CanReadTransactions). Only this ONE call of
// ReceiptProofCard's two moves to a typed client: its sibling POST .../receipts/verify is genuinely
// AllowAnonymous and deliberately stays on the ambient client.
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Verification.IVerificationBundleClient,
                               Sorcha.UI.Core.Services.User.Verification.VerificationBundleClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthenticatedHttpMessageHandler>();

// Feature 128 US3 — PWA-installability probe used by PairingHandoffSurface
// to switch between the QR variant and the install-flavoured variant.
// Singleton so the cached verdict survives across surface re-renders.
builder.Services.AddSingleton<Sorcha.UI.Core.Services.User.Pairing.IPwaInstallabilityProbe,
                              Sorcha.UI.Core.Services.User.Pairing.PwaInstallabilityProbe>();

builder.Services.AddSingleton(TimeProvider.System);

var host = builder.Build();

// Eagerly load default (English) translations before the first render so that
// translation keys (e.g. "nav.signIn") never flash as raw text in the UI.
var localization = host.Services.GetRequiredService<ILocalizationService>();
await localization.LoadDefaultTranslationsAsync();

// Run the WASM application
await host.RunAsync();
