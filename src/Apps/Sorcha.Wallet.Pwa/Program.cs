// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Wallet.Pwa;
using Sorcha.Wallet.Pwa.Extensions;
using Sorcha.Wallet.Pwa.Services.Signing;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddMudServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Feature 163: shared user-facing component library seams.
builder.Services.AddSorchaUserComponents(builder.Configuration);

// Gateway base address for typed Sorcha service clients. The wallet PWA is
// mounted at /wallet/ behind the API Gateway; gateway-relative routes start
// at the host root so we strip the /wallet/ prefix off the BaseAddress.
var hostRoot = new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority) + "/";
builder.Services.AddCitizenWalletServices(hostRoot);

// Feature 164 B3 (US2): override stub transport with the live HAIP transport + ephemeral identity.
// These registrations appear AFTER AddSorchaUserComponents (which uses TryAdd) and
// AddCitizenWalletServices (which registers IEphemeralVerifierIdentityService) so the
// DI overrides win and the ephemeral identity service is available (R4).
builder.Services.AddScoped<IVerifierIdentityProvider, EphemeralVerifierIdentityAdapter>();
builder.Services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

await builder.Build().RunAsync();
