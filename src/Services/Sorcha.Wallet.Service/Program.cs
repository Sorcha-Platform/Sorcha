// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sorcha.Wallet.Service.Extensions;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.GrpcServices;
using Sorcha.Wallet.Service.Services;
using Sorcha.ServiceClients.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Kestrel on plaintext HTTP can't multiplex HTTP/1.1 + HTTP/2 on one port
// because h2c needs ALPN which needs TLS. Bind REST on the main HTTP port and
// gRPC on a dedicated HTTP/2-only port. Same pattern as Peer.Service.
var httpPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), out var envHttpPort) ? envHttpPort : 8080;
var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 5001);
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.ListenAnyIP(httpPort, lo => lo.Protocols = HttpProtocols.Http1);
    opts.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
});
// Clear ASPNETCORE_URLS so our explicit Listen bindings aren't appended to
// default HTTP/1.1 endpoints that would miss HTTP/2 entirely.
builder.WebHost.UseUrls();

// Add Aspire service defaults (health checks, OpenTelemetry, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add Wallet Service infrastructure and domain services
builder.Services.AddWalletService(builder.Configuration);

// Add DID resolvers for credential verification
builder.Services.AddDidResolvers();

// Add presentation request service (OID4VP)
builder.Services.AddSingleton<IPresentationRequestService, PresentationRequestService>();

// Feature 047: Address registration service (US1) + notification pipeline (US2)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IAddressRegistrationService,
    Sorcha.Wallet.Service.Services.Implementation.AddressRegistrationService>();
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.INotificationRateLimiter,
    Sorcha.Wallet.Service.Services.Implementation.NotificationRateLimiter>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<Sorcha.Wallet.Service.Services.Implementation.TenantNotificationPreferenceProvider>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.INotificationPreferenceProvider,
    Sorcha.Wallet.Service.Services.Implementation.TenantNotificationPreferenceProvider>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.INotificationDeliveryService,
    Sorcha.Wallet.Service.Services.Implementation.NotificationDeliveryService>();

// Singleton TrustServiceClient — 5-min cert cache must survive across requests,
// so the HttpClient is captured once. PooledConnectionLifetime caps connection
// age at 2 min so DNS / mTLS rotation lands via connection recycling.
builder.Services.AddHttpClient("trust-service", (sp, http) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var address = config["ServiceClients:TenantService:Address"]
        ?? "https+http://tenant-service";
    http.BaseAddress = new Uri(address.TrimEnd('/') + "/");
})
.ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
});
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.IOrgCertChainProvider>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var http = factory.CreateClient("trust-service");
    var logger = sp.GetRequiredService<ILogger<Sorcha.ServiceClients.Trust.TrustServiceClient>>();
    return new Sorcha.ServiceClients.Trust.TrustServiceClient(http, logger);
});

// Feature 060: Wallet recovery services
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IPasskeyRecoveryService,
    Sorcha.Wallet.Service.Services.Implementation.PasskeyRecoveryService>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IOrgRecoveryService,
    Sorcha.Wallet.Service.Services.Implementation.OrgRecoveryService>();

// Feature 047: Notification metrics (T047 — observability)
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Implementation.NotificationMetrics>();

// Feature 106: Inbound credential detection (Wave B)
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Implementation.InboundCredentialDetectorMetrics>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IInboundCredentialDetector,
    Sorcha.Wallet.Service.Services.Implementation.InboundCredentialDetector>();

// Feature 047: Digest notification batching (US5)
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.Implementation.NotificationDigestWorker>();

// Feature 079: Transaction lifecycle tracking (TRUST-3/4/5)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ITransactionLifecycleService,
    Sorcha.Wallet.Service.Services.Implementation.TransactionLifecycleService>();
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.Implementation.TransactionLifecycleEventBridge>();

// Feature 083: Org key derivation services
builder.Services.AddSingleton<Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider,
    Sorcha.Wallet.Service.Services.Implementation.SoftwareKeyProtectionProvider>();
builder.Services.AddScoped<Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyDerivationService,
    Sorcha.Wallet.Service.Services.Implementation.OrgKeyDerivationService>();

// Feature 092: Consumer persona crypto (per-user persona vault encryption)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IPersonaCryptoService,
    Sorcha.Wallet.Service.Services.Implementation.PersonaCryptoService>();

// Feature 094: Holder binding key (KB-JWT signing for SD-JWT VC presentations)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IHolderBindingKeyService,
    Sorcha.Wallet.Service.Services.Implementation.HolderBindingKeyService>();

// Feature 094: HAIP issuer classical co-key (PQC wallets issue HAIP-compliant credentials)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IHaipIssuerCoKeyService,
    Sorcha.Wallet.Service.Services.Implementation.HaipIssuerCoKeyService>();

// Feature 114: Citizen wallet holder key (per-citizen identity for offline OID4VP wallets)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IHolderKeyService,
    Sorcha.Wallet.Service.Services.Implementation.HolderKeyService>();

// Feature 114: Citizen device delegation revocation — IETF Token Status List 2024 publisher
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenStatusListPublisher,
    Sorcha.Wallet.Service.Services.Implementation.CitizenStatusListPublisher>();

// Feature 114: Device delegation credential issuer (SD-JWT VC, signed by holder key)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceDelegationIssuer,
    Sorcha.Wallet.Service.Services.Implementation.DeviceDelegationIssuer>();

// Feature 114: Per-org citizen status-list signing wallet resolver (lazy system-wallet provisioner)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IOrgStatusSigningWalletResolver,
    Sorcha.Wallet.Service.Services.Implementation.OrgStatusSigningWalletResolver>();

// Feature 114: Hourly status-list freshness worker — keeps lists signed within their 24h exp
// even when no revocation events occur (eventful path is covered by AllocateIndexAsync / FlipAsync).
builder.Services.AddHostedService<
    Sorcha.Wallet.Service.Services.Implementation.CitizenStatusListPublisherService>();

// Feature 114: Citizen wallet sync surface (T103). EmptyCitizenCredentialEventStream is a
// placeholder until the citizen-credential issuance pipeline lands (US4 / Phase 6); the sync
// surface, sync-token round-trip, and 410 stale-cursor path are still exercised end-to-end.
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.ICitizenCredentialEventStream,
    Sorcha.Wallet.Service.Services.Implementation.EmptyCitizenCredentialEventStream>();
builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.ICitizenSyncService,
    Sorcha.Wallet.Service.Services.Implementation.CitizenSyncService>();

// Feature 114: Delegation renewal (T106). Composes Tenant Service device lookup
// + IDeviceDelegationIssuer + IOrgStatusSigningWalletResolver behind one
// idempotent re-issuance call.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDelegationRenewalService,
    Sorcha.Wallet.Service.Services.Implementation.DelegationRenewalService>();

// Feature 114 (US3): citizen device revocation. Shared between the public
// PWA-facing DELETE endpoint and the internal Tenant→Wallet S2S endpoint.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceRevocationService,
    Sorcha.Wallet.Service.Services.Implementation.DeviceRevocationService>();

// Feature 114: FluentValidation for citizen wallet request DTOs
builder.Services.AddValidatorsFromAssemblyContaining<
    Sorcha.CitizenWallet.Abstractions.Validators.DeviceEnrolmentRequestValidator>();

// Feature 114: SignalR for citizen wallet push notifications
builder.Services.AddSignalR();

// File reassembly service (US2 — File Download)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IFileReassemblyService,
    Sorcha.Wallet.Service.Services.Implementation.FileReassemblyService>();

// Add Redis for notification rate limiting and pub/sub
builder.AddRedisClient("redis");

// Add service clients for inter-service communication
builder.Services.AddServiceClients(builder.Configuration);

// Add gRPC services for inter-service communication (Validator, Peer, etc.)
builder.Services.AddGrpc();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Wallet Service API", "Cryptographic wallet management and transaction signing with HD wallets (BIP32/39/44), multi-algorithm support (ED25519, P-256, RSA-4096), and secure key storage.");

// Add Wallet Service health checks
builder.Services.AddHealthChecks()
    .AddWalletServiceHealthChecks(builder.Configuration);

// Configure CORS - production restriction handled at API Gateway (YARP)
builder.AddSorchaCors();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddWalletAuthorization();

var app = builder.Build();

// Apply database migrations automatically (only if PostgreSQL is configured)
await app.Services.ApplyWalletDatabaseMigrationsAsync();

// Map default Aspire endpoints (/health, /alive)
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001) -- must precede input validation
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Wallet Service");

// Enable CORS
app.UseCors();

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Map gRPC services for inter-service communication
app.MapGrpcService<WalletGrpcService>();
app.MapGrpcService<WalletNotificationGrpcService>();

// Map Wallet API endpoints
app.MapWalletEndpoints();
app.MapDelegationEndpoints();
app.MapCredentialEndpoints();
app.MapPresentationEndpoints();
app.MapOrgKeyEndpoints();
app.MapFileDownloadEndpoints();
app.MapPersonaCryptoEndpoints();

// Feature 114: Public citizen-device status list endpoint
app.MapCitizenStatusListEndpoints();

// Feature 114: Citizen wallet PWA endpoints (device enrolment, sync, etc.)
app.MapCitizenWalletEndpoints();
app.MapCitizenStatusListInternalEndpoints();

// Feature 114: Citizen wallet SignalR hub. Routed via API Gateway as `/hubs/wallet`.
app.MapHub<Sorcha.Wallet.Service.Hubs.WalletHub>("/hubs/wallet");

// ===========================
// Statistics Endpoint (public, no auth)
// ===========================

app.MapGet("/api/stats", async (
    Sorcha.Wallet.Core.Repositories.Interfaces.IWalletRepository walletRepository) =>
{
    try
    {
        var walletCount = await walletRepository.CountAsync();
        return Results.Ok(new { walletCount });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to get wallet statistics");
        return Results.Ok(new { walletCount = 0 });
    }
})
.WithName("GetWalletStats")
.WithSummary("Get wallet statistics (public)")
.WithDescription("Returns aggregate wallet count. No authentication required.")
.WithTags("Statistics")
.AllowAnonymous();

app.Run();

// Make the implicit Program class public for integration tests
public partial class Program { }
