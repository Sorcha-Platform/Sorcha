// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.Wallet.Pwa;
using Sorcha.Wallet.Pwa.Extensions;

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
builder.Services.AddCitizenWalletServices(hostRoot);

await builder.Build().RunAsync();
