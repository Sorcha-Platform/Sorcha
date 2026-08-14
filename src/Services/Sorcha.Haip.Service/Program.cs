// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache.Extensions;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceClients.Configuration;

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
// Feature 181 US3 — trustlist trust source over imported ETSI TS 119 612 snapshots. Constructed with
// its own TrustListAnchorProvider (distinct from the singleton x509-tenant ConfiguredTenantTrustAnchorProvider).
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.ITrustListProvider,
    Sorcha.ServiceClients.Trust.HttpTrustListProvider>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.TrustListSourceResolver(
        new Sorcha.Haip.Service.Services.TrustListAnchorProvider(
            sp.GetRequiredService<Sorcha.ServiceClients.Trust.ITrustListProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.Haip.Service.Services.TrustListAnchorProvider>>())));
builder.Services.AddHttpClient(Sorcha.ServiceClients.Trust.HttpTrustListProvider.HttpClientName, client =>
{
    client.BaseAddress = new Uri(
        SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
        ?? "http://tenant-service:8080");
});
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

// Feature 135 (US3) — org cert chain fetch for x5c-attach on x509-anchored issuance (mirrors the
// Wallet Service wiring). HAIP resolves the issuing org's leaf+root from the Tenant Service.
builder.Services.AddHttpClient("trust-service", (sp, http) =>
{
    var address = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant) ?? "https+http://tenant-service";
    http.BaseAddress = new Uri(address.TrimEnd('/') + "/");
});
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.IOrgCertChainProvider>(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("trust-service");
    return new Sorcha.ServiceClients.Trust.TrustServiceClient(
        http, sp.GetRequiredService<ILogger<Sorcha.ServiceClients.Trust.TrustServiceClient>>());
});

// Feature 135 (US2) — mso_mdoc verification. MdocFormatHandler runs the ISO 18013-5 format crypto
// and routes the trust decision through the same scoped ITrustEvaluator (x509-tenant source over
// the configured anchors). The direct_post endpoint dispatches mdoc vp_tokens to this verifier.
builder.Services.AddSingleton<Sorcha.Mdoc.IMdocService, Sorcha.Mdoc.MdocService>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.MdocFormatHandler>();
builder.Services.AddScoped<MdocPresentationVerifier>();

// Feature 181 US6 (T052) — the verifier's request-signing certificate (SAN dNSName = Haip:PublicHost).
// Resolved (or dev-fallback generated) once at startup; fail-fast in Production/Staging if unconfigured.
builder.Services.AddSingleton(sp => Sorcha.Haip.Service.Services.VerifierCertificate.Resolve(
    sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
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
    var blueprintAddress = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Blueprint)
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
    var walletAddress = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Wallet)
        ?? "http://wallet-service:8080";
    client.BaseAddress = new Uri(walletAddress);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Feature 149 — verifier-side anonymous DID resolution. SorchaDidResolver fetches the org's
// PUBLISHED did.json from Tenant (GET /orgs/by-did/{did}/did.json, anonymous). After re-anchoring,
// the issuer DID's vc-issuance key lives only in the Tenant document, not the wallet row — so this
// client points at the Tenant Service (was the Wallet by-address endpoint pre-149).
builder.Services.AddHttpClient("PublicWalletDid", client =>
{
    var tenantAddress = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
        ?? "https+http://tenant-service";
    client.BaseAddress = new Uri(tenantAddress.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
// Override SorchaDidResolver registration to use the constructor that takes the public-DID HttpClient.
builder.Services.AddScoped<Sorcha.ServiceClients.Did.SorchaDidResolver>(sp =>
    new Sorcha.ServiceClients.Did.SorchaDidResolver(
        sp.GetRequiredService<Sorcha.ServiceClients.Wallet.IWalletServiceClient>(),
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("PublicWalletDid"),
        sp.GetRequiredService<ILogger<Sorcha.ServiceClients.Did.SorchaDidResolver>>()));

var app = builder.Build();

// Issue #1433 — sanitized global exception handler, FIRST in the pipeline so it wraps every
// other middleware's unhandled exceptions too (see ServiceDefaults.Extensions for rationale).
app.UseSanitizedExceptionHandling();

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
