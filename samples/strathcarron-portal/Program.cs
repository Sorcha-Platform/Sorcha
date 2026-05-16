// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Sorcha.Sample.StrathcarronPortal;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Enrolment;
using Sorcha.UI.Core.Services.User.Presentation;

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
// TenantHubConnection backs EnrolPairingSignal's SignalR path. Sample
// portal registers it directly (the AddTenantHubServices extension lives
// in Sorcha.UI.Core which samples can't reference per the boundary).
// Council pages connect anonymously — the per-user group subscription
// is gated server-side, and the SignalR token-provider is null because
// the council page has no user session of its own; the polling fallback
// covers the case where the hub rejects the anonymous subscription.
builder.Services.AddScoped<TenantHubConnection>(sp =>
{
    var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
    var hubBaseUrl = $"{baseUri.Scheme}://{baseUri.Authority}";
    var logger = sp.GetRequiredService<ILogger<TenantHubConnection>>();
    return new TenantHubConnection(
        hubBaseUrl,
        accessTokenProvider: () => Task.FromResult<string?>(null),
        logger,
        inboxApi: null);
});
builder.Services.AddHttpClient<IEnrolPairingSignal, EnrolPairingSignal>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// F127 credential-gate services. The council page subscribes to
// BlueprintHub unauthenticated — the per-presentation group is keyed by a
// high-entropy nonce, so knowledge of the requestId is the auth scope.
builder.Services.AddSorchaPresentationGate(builder.HostEnvironment.BaseAddress);

builder.Services.AddSingleton(TimeProvider.System);

await builder.Build().RunAsync();
