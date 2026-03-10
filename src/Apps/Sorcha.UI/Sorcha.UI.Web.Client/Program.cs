// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Sorcha.Blueprint.Schemas;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Services;
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

// Add schema library service with caching (for designer and schema library pages)
builder.Services.AddScoped<ISchemaCacheService, LocalStorageSchemaCacheService>();
builder.Services.AddScoped<SchemaLibraryService>(sp =>
{
    var cacheService = sp.GetRequiredService<ISchemaCacheService>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var schemaLibrary = new SchemaLibraryService(cacheService, loggerFactory);

    var httpClient = sp.GetRequiredService<HttpClient>();

    // Add Blueprint Service repository for system and custom schemas (with ETag caching)
    schemaLibrary.AddRepository(new BlueprintServiceRepository(httpClient));

    // Add SchemaStore repository for external schemas
    var schemaStoreLogger = loggerFactory.CreateLogger<SchemaStoreRepository>();
    schemaLibrary.AddRepository(new SchemaStoreRepository(httpClient, schemaStoreLogger));

    return schemaLibrary;
});

var host = builder.Build();

// Run the WASM application
await host.RunAsync();
