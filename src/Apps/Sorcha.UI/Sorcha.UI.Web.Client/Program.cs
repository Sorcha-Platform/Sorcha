// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.Blueprint.Schemas.Client;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Enrolment;
using Sorcha.UI.Web.Client;
using Sorcha.UI.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Register root components for standalone WASM
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register core services (authentication, encryption, configuration) with base address
builder.Services.AddCoreServices(builder.HostEnvironment.BaseAddress);

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
builder.Services.AddHttpClient<ITierProbeService, HttpTierProbeService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// Feature 128 — shared has-any-device probe. Drives the PairingNagBanner
// on every MainLayout render for signed-in citizens with zero paired
// devices (FR-024). The same probe is also registered in the PWA
// (Sorcha.Wallet.Pwa) where it drives the takeover trigger (sub-PR A3).
builder.Services.AddHttpClient<Sorcha.UI.Core.Services.User.Devices.IHasPairedDeviceProbe,
                               Sorcha.UI.Core.Services.User.Devices.HasPairedDeviceProbe>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

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
