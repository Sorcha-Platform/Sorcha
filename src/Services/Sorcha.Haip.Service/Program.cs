// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (health checks, telemetry, service discovery)
builder.AddServiceDefaults();

// Add OpenAPI with Scalar documentation
builder.AddSorchaOpenApi("Sorcha HAIP Service API",
    "OpenID4VCI issuer endpoint for HAIP-compliant external wallet credential issuance.");

// Add Redis for transient HAIP state (pre-auth codes, nonces, access tokens)
builder.AddRedisClient("redis");

// Add JWT authentication (for service-to-service calls on internal endpoints)
builder.AddJwtAuthentication();

// Add rate limiting
builder.AddRateLimiting();

// Feature 097: HAIP services
builder.Services.AddSingleton<PreAuthCodeStore>();
builder.Services.AddSingleton<NonceStore>();
builder.Services.AddSingleton<CredentialOfferService>();

var app = builder.Build();

// OpenAPI and Scalar UI
app.MapSorchaOpenApiUi("Sorcha HAIP Service API");

// Standard middleware pipeline
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map Aspire default endpoints (health, alive)
app.MapDefaultEndpoints();

// Map HAIP endpoints
app.MapMetadataEndpoints();

app.Run();
