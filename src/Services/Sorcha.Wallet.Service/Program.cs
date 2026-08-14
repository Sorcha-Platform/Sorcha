// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sorcha.Wallet.Service.Extensions;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.GrpcServices;
using Sorcha.Wallet.Service.Hubs;
using Sorcha.Wallet.Service.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.ServiceClients.Configuration;

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

// Feature 118 / US3 follow-up #2 — wire WalletInboxWriter so credential issuance
// also produces durable inbox entries via Tenant Service.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Implementation.IWalletInboxWriter,
    Sorcha.Wallet.Service.Services.Implementation.WalletInboxWriter>();

// Phase 2 of the Snackbar retirement — wallet-lifecycle events (created,
// recovered, deleted, address registered) also drop durable inbox entries.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Implementation.IWalletWorkflowInboxWriter,
    Sorcha.Wallet.Service.Services.Implementation.WalletWorkflowInboxWriter>();

// Phase 2c of the Snackbar retirement — citizen-wallet device revocation
// drops a Category=Security inbox entry on the owning citizen.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Implementation.ICitizenDeviceInboxWriter,
    Sorcha.Wallet.Service.Services.Implementation.CitizenDeviceInboxWriter>();

// Singleton TrustServiceClient — 5-min cert cache must survive across requests,
// so the HttpClient is captured once. PooledConnectionLifetime caps connection
// age at 2 min so DNS / mTLS rotation lands via connection recycling.
builder.Services.AddHttpClient("trust-service", (sp, http) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var address = SorchaServiceAddresses.TryResolve(config, SorchaService.Tenant)
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

// Multi-node audit CRITICAL #2: Inbound credential status change handler
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IInboundCredentialStatusHandler,
    Sorcha.Wallet.Service.Services.Implementation.InboundCredentialStatusHandler>();

// Feature 047: Digest notification batching (US5)
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.Implementation.NotificationDigestWorker>();

// Feature 079: Transaction lifecycle tracking (TRUST-3/4/5)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ITransactionLifecycleService,
    Sorcha.Wallet.Service.Services.Implementation.TransactionLifecycleService>();
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.Implementation.TransactionLifecycleEventBridge>();

// Feature 118 — bridge encryption-pipeline events from Blueprint Service
// (Redis publisher) onto the wallet-domain hub. Required for WalletHub to
// host the encryption surface without moving the pipeline itself.
builder.Services.AddHostedService<Sorcha.Wallet.Service.Services.EncryptionEventBridge>();

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

// Feature 181 US4: org P-256 cert-issuing key resolve + pre-hashed sign for the external X.509 rail.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IOrgIssuerCertKeyService,
    Sorcha.Wallet.Service.Services.Implementation.OrgIssuerCertKeyService>();

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

// Feature 114 / US4: Citizen wallet sync surface. Reads the citizen-scoped
// CitizenCredentialEventLog written by CitizenInboxProjector when an inbound
// credential lands in CredentialStore against a known citizen holder address.
// Replaces the v1 EmptyCitizenCredentialEventStream placeholder.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenCredentialEventStream,
    Sorcha.Wallet.Service.Services.Implementation.EfCoreCitizenCredentialEventStream>();
// M2 (review): ICitizenCredentialEventStream is on the F113 audited list
// (AuditedStorageInterfaces) but was registered with a bare AddScoped, so the storage-
// registration log, the `storage-providers` health check, and the OTel gauges never saw it —
// its audited status was inert. It has only an EF-backed implementation, so record it as
// persistent (visible to the audit; never trips the in-memory fail-fast).
builder.Services.GetStorageRegistrationLog().RegisterPersistent(
    typeof(Sorcha.Wallet.Service.Services.Interfaces.ICitizenCredentialEventStream).FullName!,
    typeof(Sorcha.Wallet.Service.Services.Implementation.EfCoreCitizenCredentialEventStream).FullName!,
    "postgres");
// Scoped (was Singleton) because it now consumes the Scoped EfCoreCitizenCredentialEventStream.
// CitizenSyncService is stateless apart from its signing key, so per-request creation is cheap.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenSyncService,
    Sorcha.Wallet.Service.Services.Implementation.CitizenSyncService>();

// Feature 114 / US4: citizen-inbox projector. Scoped because it consumes WalletDbContext.
//
// IHolderAddressLookup is NOT registered here. It used to be, unconditionally, bound to the EF Core
// implementation — which cannot be activated without a WalletDbContext, and the DbContext is
// registered only when a Postgres connection string is present. On the supported no-Postgres path
// (Pattern #13) every endpoint touching the lookup returned 500. It now lives in
// WalletServiceExtensions.AddWalletDatabase, next to the branch that decides its backend.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenInboxProjector,
    Sorcha.Wallet.Service.Services.Implementation.CitizenInboxProjector>();

// Feature 114: Delegation renewal (T106). Composes Tenant Service device lookup
// + IDeviceDelegationIssuer + IOrgStatusSigningWalletResolver behind one
// idempotent re-issuance call.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDelegationRenewalService,
    Sorcha.Wallet.Service.Services.Implementation.DelegationRenewalService>();

// Feature 114 (US3): citizen device revocation. Shared between the public
// PWA-facing DELETE endpoint and the internal Tenant→Wallet S2S endpoint.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceRevocationService,
    Sorcha.Wallet.Service.Services.Implementation.DeviceRevocationService>();

// Feature 1195 (Phase 2): device-bound credential copy cap + LRU eviction. The policy and
// its two seams (lookup + revoker) MUST be registered together — registering the policy
// without concrete seams fails Development ValidateOnBuild at boot. The coordinator is the
// mint-path entrypoint (CredentialEndpoints.IssueCredential) that runs the discriminator +
// policy + F114 status-slot allocation. All scoped (they consume WalletDbContext / the
// scoped citizen status-list + holder services).
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceBoundCredentialLookup,
    Sorcha.Wallet.Service.Services.Implementation.EfCoreDeviceBoundCredentialLookup>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceBoundCredentialRevoker,
    Sorcha.Wallet.Service.Services.Implementation.DeviceBoundCredentialRevoker>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceBoundCredentialPolicy,
    Sorcha.Wallet.Service.Services.Implementation.DeviceBoundCredentialPolicy>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IDeviceBoundCopyIssuanceCoordinator,
    Sorcha.Wallet.Service.Services.Implementation.DeviceBoundCopyIssuanceCoordinator>();

// Feature 114 (US5): citizen presentation-log reporting. The reporter dedupes
// each reported entry (Redis SET-NX, 24h) and forwards new ones via the forwarder
// seam (PR2). PR3 forwards into the durable per-citizen presentation store so the
// citizen's history follows them across devices — no Blueprint Service, no register
// write (a free-standing offline presentation has no originating register).
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenPresentationLogReporter,
    Sorcha.Wallet.Service.Services.Implementation.CitizenPresentationLogReporter>();
// Scoped (was Singleton in PR2): the forwarder now consumes the scoped store /
// WalletDbContext. The reporter is resolved inside a fresh DI scope on the report
// path, so a scoped forwarder + scoped store stay within that scope.
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IPresentationLogForwarder,
    Sorcha.Wallet.Service.Services.Implementation.CitizenPresentationStoreForwarder>();

// Feature 114 (US5 PR3): durable per-citizen presentation history store. Registered
// via IStorageRegistrationLog (RegisterPersistent with Postgres, RegisterInMemory
// fallback) but deliberately NOT on the F113 fail-fast audited list — convenience
// data, so an in-memory backend warns rather than gating startup.
{
    var presentationStoreLog = builder.Services.GetStorageRegistrationLog();
    var presentationStoreInterface =
        typeof(Sorcha.Wallet.Service.Services.Interfaces.ICitizenPresentationStore).FullName!;
    var hasWalletPostgres =
        !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:Wallet:Postgres"])
        || !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:Sorcha:Postgres"]);

    if (hasWalletPostgres)
    {
        builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.ICitizenPresentationStore,
            Sorcha.Wallet.Service.Services.Implementation.EfCoreCitizenPresentationStore>();
        presentationStoreLog.RegisterPersistent(
            presentationStoreInterface,
            typeof(Sorcha.Wallet.Service.Services.Implementation.EfCoreCitizenPresentationStore).FullName!,
            "postgres");
    }
    else
    {
        builder.Services.AddSingleton<Sorcha.Wallet.Service.Services.Interfaces.ICitizenPresentationStore,
            Sorcha.Wallet.Service.Services.Implementation.InMemoryCitizenPresentationStore>();
        presentationStoreLog.RegisterInMemory(
            presentationStoreInterface,
            typeof(Sorcha.Wallet.Service.Services.Implementation.InMemoryCitizenPresentationStore).FullName!,
            "no Postgres connection string in ConnectionStrings:Wallet:Postgres or ConnectionStrings:Sorcha:Postgres");
    }
}

// Feature 114: FluentValidation for citizen wallet request DTOs
builder.Services.AddValidatorsFromAssemblyContaining<
    Sorcha.CitizenWallet.Abstractions.Validators.DeviceEnrolmentRequestValidator>();

// Feature 124: pending-application notice store + validators in the Wallet
// Service assembly (SetPendingApplicationRequestValidator).
builder.Services.AddValidatorsFromAssemblyContaining<
    Sorcha.Wallet.Service.Validators.SetPendingApplicationRequestValidator>();
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IPendingApplicationStore,
    Sorcha.Wallet.Service.Services.Implementation.RedisPendingApplicationStore>();

// Feature 118 — multi-node hub fan-out via Redis backplane (US1).
// Wires JWT auth + Redis backplane (ChannelPrefix=sorcha:signalr:wallet) +
// reconnect-with-jitter + OpenTelemetry instrumentation.
builder.Services.AddSorchaHub<WalletHub, IWalletHubClient>(
    builder.Configuration, "/hubs/wallet", "wallet");

// File reassembly service (US2 — File Download)
builder.Services.AddScoped<Sorcha.Wallet.Service.Services.Interfaces.IFileReassemblyService,
    Sorcha.Wallet.Service.Services.Implementation.FileReassemblyService>();

// Add Redis for notification rate limiting and pub/sub
builder.AddRedisClient("redis");

// Feature 124 — IDistributedCache backing for RedisPendingApplicationStore.
// Re-uses the same "redis" connection registered above; Aspire wires the
// StackExchange.Redis distributed-cache implementation on top of the
// existing IConnectionMultiplexer.
builder.AddRedisDistributedCache("redis");

// Add service clients for inter-service communication
builder.Services.AddServiceClients(builder.Configuration);

// Add gRPC services for inter-service communication (Validator, Peer, etc.)
builder.Services.AddGrpc();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Wallet Service API", "Cryptographic wallet management and transaction signing with HD wallets (BIP32/39/44), multi-algorithm support (ED25519, P-256, RSA-4096), and secure key storage.");

// Add Wallet Service health checks
builder.Services.AddHealthChecks()
    .AddWalletServiceHealthChecks(builder.Configuration);

// Minimal-API JSON: read/write the shared SorchaJson shape (camelCase properties + kebab-case
// string enums), matching Tenant Service and what every client already sends.
//
// Without this, body binding used ASP.NET's DEFAULT options, which know only the PascalCase names
// from each enum's type-level [JsonConverter(typeof(JsonStringEnumConverter))]. The UI serialises
// with JsonDefaults.Api (SorchaJson), whose kebab converter in the Converters collection OUTRANKS
// that attribute — so it sends "fail-closed" / "sorcha-wallet" and the binder threw before the
// handler ran.
//
// Live consequence (n1, 2026-07-28): every POST /credentials/match returned 400 in ~1ms, and the
// client turns a non-success status into an empty match list — so the AIAS Cyber gate told a
// citizen holding a valid, Active, correctly-typed Assured Identity credential that they had
// "No matching credential". The endpoint had never worked from the web UI; it went unnoticed
// because M1's actions declare no credentialRequirements.
//
// Scoped deliberately to this service rather than platform-wide (see the same call in Tenant's
// Program.cs): services fronting standards surfaces — OAuth/OIDC snake_case, VC token bodies —
// must NOT get a uniform casing. Wallet's standards output (the status-list JWT, credential
// export) is written as text/pre-serialised tokens, not through minimal-API JSON serialisation,
// so it is unaffected.
builder.Services.ConfigureHttpJsonOptions(
    options => Sorcha.Serialization.SorchaJson.Configure(options.SerializerOptions));

// Configure CORS - production restriction handled at API Gateway (YARP)
builder.AddSorchaCors();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddWalletAuthorization();

var app = builder.Build();

// Issue #1433 — sanitized global exception handler, FIRST in the pipeline so it wraps every
// other middleware's unhandled exceptions too (see ServiceDefaults.Extensions for rationale).
app.UseSanitizedExceptionHandling();

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
app.MapEthereumEndpoints();
app.MapEthereumTransactionEndpoints();
app.MapCredentialEndpoints();
app.MapPresentationEndpoints();
app.MapOrgKeyEndpoints();
app.MapIssuanceKeyEndpoints();
app.MapFileDownloadEndpoints();
app.MapPersonaCryptoEndpoints();

// Feature 114: Public citizen-device status list endpoint
app.MapCitizenStatusListEndpoints();

// Feature 114: Citizen wallet PWA endpoints (device enrolment, sync, etc.)
app.MapCitizenWalletEndpoints();
app.MapCitizenStatusListInternalEndpoints();

// Feature 181 US4: org P-256 cert-issuing key resolve + sign (Tenant Service consumes these).
app.MapIssuerCertKeyInternalEndpoints();

// Feature 124: Pending-application notice endpoints (Set / Get / Clear)
app.MapPendingApplicationEndpoints();

// Feature 114: Citizen wallet SignalR hub. Routed via API Gateway as `/hubs/wallet`.
// Mapped via MapSorchaHubs from the AddSorchaHub registry (Feature 118 US1).
app.MapSorchaHubs();

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
