// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache.Extensions;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Services;
using Sorcha.ServiceClients.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults (health checks, telemetry, service discovery)
builder.AddServiceDefaults();

// Add OpenAPI with Scalar documentation
builder.AddSorchaOpenApi("Sorcha HAIP Service API",
    "OpenID4VCI issuer endpoint for HAIP-compliant external wallet credential issuance.");

// Add Redis for transient HAIP state (pre-auth codes, nonces, access tokens).
// AddRedisClient registers IConnectionMultiplexer which RedisAtomicDistributedCache picks up.
builder.AddRedisClient("redis");

// Atomic distributed cache for replay-protection state — closes the GET+DEL TOCTOU
// window in NonceStore / PreAuthCodeStore. Uses SorchaConnections cascade
// (ConnectionStrings:Haip:Redis → ConnectionStrings:Sorcha:Redis); falls back to
// in-memory in dev (audited list — Production/Staging fail-fast).
builder.Services.AddAtomicDistributedCache(builder.Configuration, "Haip");

// HAIP nonce metric meter — sorcha_haip_nonce_consume_total{store, outcome}.
// Registered as singleton; consumed by NonceStore + PreAuthCodeStore.
builder.Services.AddSingleton<HaipNonceMetrics>();

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

// Feature 135 (T032/T034) — the HAIP verifier routes its trust decision through the unified
// ITrustEvaluator shared with the internal engine path. The x5c chain validation that used to
// live on the verifier as a static trusted-root list is now the x509-tenant trust source over
// ConfiguredTenantTrustAnchorProvider (still sourced from Haip:TrustedRootCertificates). Scoped
// because the directory adapter consumes the scoped IDidResolverRegistry.
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Credentials.ITenantTrustAnchorProvider,
    Sorcha.Haip.Service.Services.ConfiguredTenantTrustAnchorProvider>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory,
    Sorcha.Haip.Service.Services.DidIssuerDirectory>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.X509TenantTrustSourceResolver(
        sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.ITenantTrustAnchorProvider>()));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.RegisterTrustSourceResolver(
        sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory>()));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.DidAllowlistTrustSourceResolver(
        sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory>()));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustResolverRegistry>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.TrustResolverRegistry(
        sp.GetServices<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>()));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.IStatusListChecker>(sp =>
    sp.GetRequiredService<IetfTokenStatusListChecker>());
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustEvaluator,
    Sorcha.Blueprint.Engine.Credentials.TrustEvaluator>();
builder.Services.AddScoped<HaipPresentationVerifier>(sp => new HaipPresentationVerifier(
    sp.GetRequiredService<Sorcha.Cryptography.SdJwt.ISdJwtService>(),
    sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.ITrustEvaluator>(),
    sp.GetRequiredService<ILogger<HaipPresentationVerifier>>(),
    sp.GetService<Sorcha.ServiceClients.Did.IDidResolverRegistry>()));
builder.Services.AddSingleton<RequestObjectSigner>();

// Feature 095 US4: status list fetch for the verifier. Registered as HttpClient-
// backed so the underlying connection pool is shared and timeouts are governed
// by the standard .NET HTTP resilience pipeline.
builder.Services.AddHttpClient<IetfTokenStatusListChecker>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Feature 111 — HAIP as a consumer of the Timebound Presentation Lifecycle.
builder.Services.AddSingleton<Sorcha.PresentationLifecycle.Abstractions.IPresentationConsumer,
    Sorcha.Haip.Service.Services.HaipPresentationConsumer>();

// PresentationCallbackRelay needs IServiceAuthClient to authenticate the s2s
// callback into Blueprint Service. Register the standard service-client stack
// (matches every other Sorcha service — Blueprint, Register, Tenant, Validator,
// Wallet, Peer all call AddServiceClients in their Program.cs).
builder.Services.AddServiceClients(builder.Configuration);

builder.Services.AddHttpClient<Sorcha.Haip.Service.Services.PresentationCallbackRelay>(client =>
{
    var blueprintAddress = builder.Configuration["ServiceClients:BlueprintService:Address"]
        ?? "http://blueprint-service:8080";
    client.BaseAddress = new Uri(blueprintAddress);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Feature 120 T039 — cross-service trigger for the wallet's lazy issuance-key
// derivation. HAIP's /credential endpoint calls this before minting so the
// org's published DID document is in place by the time a verifier resolves it.
builder.Services.AddHttpClient<
    Sorcha.ServiceClients.IssuanceKey.IIssuanceKeyClient,
    Sorcha.ServiceClients.IssuanceKey.IssuanceKeyClient>(client =>
{
    var walletAddress = builder.Configuration["ServiceClients:WalletService:Address"]
        ?? "http://wallet-service:8080";
    client.BaseAddress = new Uri(walletAddress);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Feature 120 — verifier-side anonymous DID resolution. SorchaDidResolver in HAIP
// hits wallet's /api/v1/wallets/{addr}/did-document (anonymous) so the verifier
// path doesn't need service-auth bearer just to read the public key.
builder.Services.AddHttpClient("PublicWalletDid", client =>
{
    var walletAddress = builder.Configuration["ServiceClients:WalletService:Address"]
        ?? "http://wallet-service:8080";
    client.BaseAddress = new Uri(walletAddress);
    client.Timeout = TimeSpan.FromSeconds(5);
});
// Override SorchaDidResolver registration to use the constructor that takes the public-DID HttpClient.
builder.Services.AddScoped<Sorcha.ServiceClients.Did.SorchaDidResolver>(sp =>
    new Sorcha.ServiceClients.Did.SorchaDidResolver(
        sp.GetRequiredService<Sorcha.ServiceClients.Wallet.IWalletServiceClient>(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("PublicWalletDid"),
        sp.GetRequiredService<ILogger<Sorcha.ServiceClients.Did.SorchaDidResolver>>()));

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
