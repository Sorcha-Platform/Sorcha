// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.UI.Core.Services;
using Sorcha.Wallet.Pwa;
using Sorcha.Wallet.Pwa.Extensions;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Signing;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddMudServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Gateway base address for typed Sorcha service clients. The wallet PWA is
// mounted at /wallet/ behind the API Gateway; gateway-relative routes start
// at the host root so we strip the /wallet/ prefix off the BaseAddress.
var hostRoot = new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority) + "/";

// Feature 163/164: shared user-facing component library seams. Pass hostRoot so the
// IHaipVerifierClient typed HttpClient gets a BaseAddress — WASM IHttpClientFactory
// clients have no BaseAddress by default, causing relative URI calls to throw — and attach the
// PWA's BearerTokenHandler + ServerClockHandler so the verify request carries the holder's bearer
// token. The verify endpoints (/api/v1/verifier/requests) require an authenticated caller; without
// the handler the call 401s and is mis-rendered as "Verification is not yet configured here."
builder.Services.AddSorchaUserComponents(
    builder.Configuration,
    haipBaseUrl: hostRoot,
    configureHaipClient: b => b
        .AddHttpMessageHandler<BearerTokenHandler>()
        .AddHttpMessageHandler<ServerClockHandler>());

builder.Services.AddCitizenWalletServices(hostRoot);

// Feature 164 B3 (US2): override stub transport with the live HAIP transport + ephemeral identity.
// These registrations appear AFTER AddSorchaUserComponents (which uses TryAdd) and
// AddCitizenWalletServices (which registers IEphemeralVerifierIdentityService) so the
// DI overrides win and the ephemeral identity service is available (R4).
builder.Services.AddScoped<IVerifierIdentityProvider, EphemeralVerifierIdentityAdapter>();
builder.Services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

var host = builder.Build();

// Bug fix — the PWA registers ILocalizationService (Extensions/ServiceCollectionExtensions.cs)
// but never initialised it, so every Loc.T(key) call anywhere in the shared component library
// (Settings → Security is the page a citizen actually hit this on) rendered the raw dot-notation
// key instead of text. Mirrors the web host (Sorcha.UI.Web.Client/Program.cs), which loads
// default (English) translations eagerly before the first render.
var localization = host.Services.GetRequiredService<ILocalizationService>();
await localization.LoadDefaultTranslationsAsync();

await host.RunAsync();
