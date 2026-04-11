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

// Add authorization with shared policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.RequireService, policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("client_id"));
});

// Add anti-forgery (required by .NET 10 for [FromForm] endpoints)
builder.Services.AddAntiforgery();

// Add rate limiting
builder.AddRateLimiting();

// Feature 097: HAIP issuance services
builder.Services.AddSingleton<PreAuthCodeStore>();
builder.Services.AddSingleton<NonceStore>();
builder.Services.AddSingleton<AccessTokenStore>();
builder.Services.AddSingleton<CredentialOfferService>();
builder.Services.AddSingleton<Sorcha.Cryptography.SdJwt.SdJwtService>();
builder.Services.AddSingleton<Sorcha.Cryptography.SdJwt.ISdJwtService>(sp =>
    sp.GetRequiredService<Sorcha.Cryptography.SdJwt.SdJwtService>());
builder.Services.AddSingleton<HaipCredentialMinter>();

// Feature 098: HAIP verifier services
builder.Services.AddSingleton<PresentationRequestStore>();
builder.Services.AddSingleton<HaipPresentationVerifier>();

var app = builder.Build();

// OpenAPI and Scalar UI
app.MapSorchaOpenApiUi("Sorcha HAIP Service API");

// Standard middleware pipeline
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

// Map Aspire default endpoints (health, alive)
app.MapDefaultEndpoints();

// Map HAIP endpoints
app.MapMetadataEndpoints();
app.MapTokenEndpoints();
app.MapNonceEndpoints();
app.MapCredentialEndpoints();
app.MapOfferEndpoints();
app.MapVerifierEndpoints();

app.Run();
