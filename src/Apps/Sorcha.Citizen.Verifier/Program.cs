// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using MudBlazor.Services;
using Sorcha.Citizen.Verifier.Components;
using Sorcha.Citizen.Verifier.Endpoints;
using Sorcha.Citizen.Verifier.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// Feature 114: verifier services (status list cache; VP validator + presentation
// request builder land with T088-T091).
builder.Services.AddCitizenVerifier();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapPresentationResponseEndpoints();
app.MapDemoMintEndpoint();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
