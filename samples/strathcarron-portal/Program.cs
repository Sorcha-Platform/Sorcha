// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.Sample.StrathcarronPortal;
using Sorcha.UI.Core.Services.User.Enrolment;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();
builder.Services.AddMudServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// F126 council enrolment gate services — same wiring shape as
// Sorcha.UI.Web.Client, but in a third-party-consumer host. Proves the
// library is usable without any internal-only ProjectReference.
builder.Services.AddHttpClient<ITierProbeService, HttpTierProbeService>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddSingleton(TimeProvider.System);

await builder.Build().RunAsync();
