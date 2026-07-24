// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MudBlazor.Services;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Configuration;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Components;
using Sorcha.Verifier.Endpoints;
using Sorcha.Verifier.Extensions;
using Sorcha.Verifier.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// Feature 114: verifier services (status list cache; VP validator + presentation
// request builder land with T088-T091).
builder.Services.AddCitizenVerifier(builder.Configuration);

// Feature 164 / #1189: the Open Verifier authenticates to the HAIP verifier endpoints as the
// `service-verifier` service principal (client_credentials via ServiceAuthClient).
builder.Services.AddHttpClient<ServiceAuthClient>();
builder.Services.AddSingleton<IServiceAuthClient, ServiceAuthClient>();
builder.Services.AddTransient<ServiceAuthMessageHandler>();

// Feature 163: shared user-facing component library seams (IVerificationPresetCatalogue,
// IVerificationTransport, IRegisterAnchorClient). IRegisterAnchorClient is already wired
// by AddCitizenVerifier above so AddSorchaUserComponents' guard skips re-registration.
var haipBaseUrl = builder.Configuration["Verifier:HaipBaseUrl"]
    ?? SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Haip)
    ?? "http://haip-service:8080";
builder.Services.AddSorchaUserComponents(
    builder.Configuration,
    haipBaseUrl: haipBaseUrl,
    configureHaipClient: b => b.AddHttpMessageHandler<ServiceAuthMessageHandler>());

// Feature 164 B3 (US3): override stub transport with the live HAIP transport + stable org identity.
// These registrations appear AFTER AddSorchaUserComponents (which uses TryAdd) so the DI overrides
// win (R4). StableOrgVerifierIdentityProvider reads Verifier:OrgId from appsettings.
builder.Services.AddScoped<IVerifierIdentityProvider, StableOrgVerifierIdentityProvider>();
builder.Services.AddScoped<IVerificationTransport, HaipVerificationTransport>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapDemoMintEndpoint();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
