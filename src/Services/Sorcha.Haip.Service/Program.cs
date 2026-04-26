// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.AtomicCache.Extensions;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Services;

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
builder.Services.AddSingleton<HaipPresentationVerifier>(sp =>
{
    // Feature 096 US6 — deployments with reachable CRL endpoints opt in with
    // Haip:VerifyRevocation=true. Default off so the chain walk doesn't block
    // on dead CDP URLs in test/dev environments.
    var revocationMode = builder.Configuration.GetValue<bool>("Haip:VerifyRevocation")
        ? System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
        : System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

    var verifier = new HaipPresentationVerifier(
        sp.GetRequiredService<Sorcha.Cryptography.SdJwt.ISdJwtService>(),
        sp.GetRequiredService<ILogger<HaipPresentationVerifier>>(),
        sp.GetService<Sorcha.ServiceClients.Did.IDidResolverRegistry>(),
        sp.GetService<IetfTokenStatusListChecker>(),
        revocationMode);

    // Feature 096 US6 — load trusted root CA certs from config. Deployments
    // list them under `Haip:TrustedRootCertificates` as base64-DER strings.
    // Without at least one root, x5c chain validation will reject every cert.
    var configuredRoots = builder.Configuration
        .GetSection("Haip:TrustedRootCertificates")
        .Get<string[]>() ?? Array.Empty<string>();
    var logger = sp.GetRequiredService<ILogger<HaipPresentationVerifier>>();
    foreach (var rootBase64 in configuredRoots)
    {
        if (string.IsNullOrWhiteSpace(rootBase64))
            continue;
        try
        {
            var der = Convert.FromBase64String(rootBase64);
            var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
            verifier.AddTrustedRoot(cert);
            logger.LogInformation(
                "Loaded trusted root CA into HAIP verifier: {Subject} (NotAfter={NotAfter})",
                cert.Subject, cert.NotAfter);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to load a Haip:TrustedRootCertificates entry — skipping and continuing");
        }
    }
    return verifier;
});
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
builder.Services.AddHttpClient<Sorcha.Haip.Service.Services.PresentationCallbackRelay>(client =>
{
    var blueprintAddress = builder.Configuration["ServiceClients:BlueprintService:Address"]
        ?? "http://blueprint-service:8080";
    client.BaseAddress = new Uri(blueprintAddress);
    client.Timeout = TimeSpan.FromSeconds(15);
});

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
