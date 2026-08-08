// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.OData;
using Microsoft.OData.ModelBuilder;
using MongoDB.Driver;
using Scalar.AspNetCore;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Extensions;
using Sorcha.Register.Service.Hubs;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.Register.Service.Services;
using Microsoft.Extensions.Options;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Register.Storage.MongoDB;
using Sorcha.Register.Storage.Redis;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Register.Service.Endpoints;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.Validator.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.ServiceClients.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Kestrel on plaintext HTTP can't multiplex HTTP/1.1 + HTTP/2 on one port
// (h2c needs ALPN, which needs TLS). Bind REST on 8080 and gRPC on a
// dedicated HTTP/2-only port for RegisterAddressGrpcService.
{
    var httpPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), out var envHttpPort) ? envHttpPort : 8080;
    var grpcPort = builder.Configuration.GetValue<int>("Kestrel:GrpcPort", 5001);
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.ListenAnyIP(httpPort, lo => lo.Protocols = HttpProtocols.Http1);
        opts.ListenAnyIP(grpcPort, lo => lo.Protocols = HttpProtocols.Http2);
    });
    builder.WebHost.UseUrls();
}

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Feature 118 — multi-node hub fan-out via Redis backplane (US1).
// Wires JWT auth + Redis backplane (ChannelPrefix=sorcha:signalr:register) +
// reconnect-with-jitter + OpenTelemetry instrumentation.
// RegisterHub does not yet have [Authorize] — that lands in Phase 6 (FR-011)
// after the UI client ships token-passing one release earlier.
builder.Services.AddSorchaHub<RegisterHub, IRegisterHubClient>(
    builder.Configuration, "/hubs/register", "register");

// Configure OData
var modelBuilder = new ODataConventionModelBuilder();
modelBuilder.EntitySet<Sorcha.Register.Models.Register>("Registers");
modelBuilder.EntitySet<TransactionModel>("Transactions");
modelBuilder.EntitySet<DocketHeader>("Dockets");

builder.Services.AddControllers()
    .AddOData(options => options
        .Select()
        .Filter()
        .OrderBy()
        .Expand()
        .Count()
        .SetMaxTop(100)
        .AddRouteComponents("odata", modelBuilder.GetEdmModel()));

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Register Service API", "Distributed ledger for storing immutable transaction records with cryptographic chain integrity, OData queries, SignalR real-time notifications, and wallet-based payload encryption.");

// Register storage and event infrastructure
// MongoDB client — shared by register storage (when configured) and system register
builder.Services.Configure<MongoRegisterStorageConfiguration>(
    builder.Configuration.GetSection("RegisterStorage:MongoDB"));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
    var connectionString = !string.IsNullOrWhiteSpace(config.ConnectionString)
        ? config.ConnectionString
        : builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017";
    return new MongoClient(connectionString);
});

// Smart configuration: Use MongoDB if configured, otherwise InMemory
var storageType = builder.Configuration["RegisterStorage:Type"] ?? "InMemory";
var storageLog = builder.Services.GetStorageRegistrationLog();
var registerInterfaceName = typeof(IRegisterRepository).FullName!;
if (storageType.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IRegisterRepository>(sp =>
    {
        var client = sp.GetRequiredService<IMongoClient>();
        var options = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>();
        var logger = sp.GetRequiredService<ILogger<MongoRegisterRepository>>();
        return new MongoRegisterRepository(client, options, logger);
    });
    storageLog.RegisterPersistent(
        registerInterfaceName,
        typeof(MongoRegisterRepository).FullName!,
        "mongo");

    // Register the same instance as IReadOnlyRegisterRepository
    builder.Services.AddSingleton<IReadOnlyRegisterRepository>(sp =>
        sp.GetRequiredService<IRegisterRepository>());
}
else
{
    // Use in-memory storage (default)
    builder.Services.AddSingleton<IRegisterRepository, InMemoryRegisterRepository>();
    storageLog.RegisterInMemory(
        registerInterfaceName,
        typeof(InMemoryRegisterRepository).FullName!,
        "RegisterStorage:Type is not 'MongoDB' (default 'InMemory'). Set RegisterStorage:Type=MongoDB to enable persistent storage.");

    // Register the same instance as IReadOnlyRegisterRepository
    builder.Services.AddSingleton<IReadOnlyRegisterRepository>(sp =>
        sp.GetRequiredService<IRegisterRepository>());
}

// Event infrastructure: Redis Streams for durable event publishing/subscribing
builder.Services.AddRedisEventStreams(builder.Configuration);

// Register managers
builder.Services.AddScoped<RegisterManager>();
builder.Services.AddScoped<TransactionManager>();
builder.Services.AddScoped<QueryManager>();

// Feature 108 — local relationship, observation intake, sync-state resolver.
builder.Services.Configure<Sorcha.Register.Core.LocalRelationship.LocalIdentityOptions>(
    builder.Configuration.GetSection("LocalIdentity"));
builder.Services.Configure<Sorcha.Register.Core.SyncState.RegisterSyncStateOptions>(
    builder.Configuration.GetSection("RegisterSyncState"));
// Resolve this node's identity from the system wallet it actually signs with, not from static
// config. LocalIdentity:* is set on no deployment, so the configured provider resolved an EMPTY
// identity and every register derived as Subscriber — a node was a "subscriber" to the very
// registers it owns, validates and seals. Configured values still win when present (dev/test seam);
// the wallet is the fallback, and an unreachable wallet fails safe to Subscriber.
builder.Services.AddSingleton<
    Sorcha.Register.Core.LocalRelationship.ILocalIdentityProvider,
    Sorcha.Register.Service.Services.SystemWalletLocalIdentityProvider>();
builder.Services.AddSingleton<
    Sorcha.Register.Core.LocalRelationship.IRegisterLocalRelationshipService,
    Sorcha.Register.Core.LocalRelationship.RegisterLocalRelationshipService>();
builder.Services.AddSingleton<
    Sorcha.Register.Core.Observations.IObservationStore,
    Sorcha.Register.Core.Observations.ObservationStore>();
builder.Services.AddSingleton<
    Sorcha.Register.Core.SyncState.IRegisterSyncStateResolver,
    Sorcha.Register.Core.SyncState.RegisterSyncStateResolver>();
builder.Services.AddSingleton<Sorcha.Register.Service.Services.RelationshipChangeNotifier>();
builder.Services.AddHostedService<Sorcha.Register.Service.BackgroundServices.ObservationStorePruner>();

// Register creation orchestration
builder.Services.AddScoped<IRegisterCreationOrchestrator, RegisterCreationOrchestrator>();

// Redis client for distributed state (pending registrations, caching)
builder.AddRedisClient("redis");

// Pending registration storage (Redis-backed for multi-instance deployments)
builder.Services.AddSingleton<IPendingRegistrationStore, PendingRegistrationStore>();

// Register cryptography services (from Sorcha.Cryptography)
builder.Services.AddScoped<IHashProvider, Sorcha.Cryptography.Core.HashProvider>();
builder.Services.AddScoped<ICryptoModule, Sorcha.Cryptography.Core.CryptoModule>();

// Feature 189 (FR-035): re-derives a wallet address from an offered public key, so an approval
// naming an accountable individual can be checked against the key that actually signed it rather
// than taken on the signer's word. Singleton to match every other host that registers it.
builder.Services.AddSingleton<Sorcha.Cryptography.Interfaces.IWalletUtilities,
    Sorcha.Cryptography.Utilities.WalletUtilities>();

// Feature 188: Provenance — trust-anchor and proof lineage (read-only).
// The trust anchor is a deploy-time fact read once (singleton); the metrics meter is likewise
// process-wide. The resolver, assembler and Merkle seam are scoped alongside the repository they
// read through. MerkleRootCalculator DELEGATES to the platform's one MerkleTree — see its remarks
// for why a second implementation here would surface as a false tamper report.
builder.Services.AddSingleton<Sorcha.Register.Service.Provenance.INodeTrustAnchor,
    Sorcha.Register.Service.Provenance.NodeTrustAnchor>();
builder.Services.AddSingleton<Sorcha.Register.Service.Provenance.ProvenanceMetrics>();
builder.Services.AddScoped<Sorcha.Register.Service.Provenance.IRosterAsOfResolver,
    Sorcha.Register.Service.Provenance.RosterAsOfResolver>();
builder.Services.AddScoped<Sorcha.Register.Service.Provenance.IDocketEvidenceAssembler,
    Sorcha.Register.Service.Provenance.DocketEvidenceAssembler>();
builder.Services.AddScoped<Sorcha.Provenance.Engine.Seams.IMerkleRootCalculator,
    Sorcha.Register.Service.Provenance.MerkleRootCalculator>();

// Register wallet service client
builder.Services.AddServiceClients(builder.Configuration);

// Tenant Service internal subscription client. After finalising a register, the
// Register Service immediately subscribes the owning organisation via a
// service-to-service call — this removes the old client-side admin-gated hop
// that blocked service-principal callers from seeing their own registers.
builder.Services.AddHttpClient<
    Sorcha.Register.Service.Services.ITenantSubscriptionClient,
    Sorcha.Register.Service.Services.TenantSubscriptionClient>(client =>
{
    var tenantBase = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
        ?? "http://tenant-service";
    client.BaseAddress = new Uri(tenantBase);
});

// Register system wallet signing service (opt-in — used for genesis + blueprint publish)
builder.Services.AddSystemWalletSigning(builder.Configuration);

// Register crypto policy service
builder.Services.AddScoped<Sorcha.Register.Service.Services.CryptoPolicyService>();

// Register governance roster service
builder.Services.AddScoped<Sorcha.Register.Core.Services.IGovernanceRosterService,
    Sorcha.Register.Core.Services.GovernanceRosterService>();

// Feature 189: signs governance control transactions as an ORGANISATION on the register's roster
// (slot 100), not as the node. The node's system wallet is on no roster, so a node-signed
// governance transaction is refused by the Validator on any register whose genesis has sealed.
builder.Services.AddScoped<Sorcha.Register.Service.Services.IGovernanceSigningService,
    Sorcha.Register.Service.Services.GovernanceSigningService>();

// Feature 189 US2: produces cryptographically signed approvals. Without a producer, US2-A's
// mandatory verification would leave every quorum-requiring operation unsatisfiable.
builder.Services.AddScoped<Sorcha.Register.Service.Services.IGovernanceApprovalService,
    Sorcha.Register.Service.Services.GovernanceApprovalService>();

// Feature 189 T078: verifies an approval produced outside the platform's trust boundary — every
// signature, and that the key naming an accountable individual actually belongs to them (FR-035).
builder.Services.AddScoped<Sorcha.Register.Service.Services.IDetachedApprovalVerifier,
    Sorcha.Register.Service.Services.DetachedApprovalVerifier>();

// Feature 189 T075: carries a verified approval to the ledger as an action submission of the
// governance blueprint — through the Validator, never straight to storage.
builder.Services.AddScoped<Sorcha.Register.Service.Services.IGovernanceApprovalActionSubmitter,
    Sorcha.Register.Service.Services.GovernanceApprovalActionSubmitter>();

// Feature 048: Register policy service (reads policy from control chain via direct repository access)
builder.Services.AddScoped<Sorcha.Register.Core.Services.ISystemBlueprintValidator,
    Sorcha.Register.Service.Services.SystemBlueprintValidator>();
builder.Services.AddScoped<Sorcha.Register.Core.Services.IRegisterPolicyService,
    Sorcha.Register.Core.Services.RegisterPolicyService>();

// Register system register services (scoped — will use ledger-backed dependencies)
builder.Services.AddScoped<SystemRegisterService>();
builder.Services.AddSingleton<StructuralDiffService>();

// Feature 099: Genesis trust anchor — load pre-signed genesis, verify signature
builder.Services.Configure<Sorcha.ServiceDefaults.SystemRegisterOptions>(
    builder.Configuration.GetSection(Sorcha.ServiceDefaults.SystemRegisterOptions.SectionName));
builder.Services.AddScoped<GenesisIngestionService>();

// System register bootstrap — ingests pre-signed genesis (never creates at runtime)
builder.Services.AddHostedService<SystemRegisterBootstrapper>();

// Participant index service (in-memory address → participant mapping)
builder.Services.AddSingleton<ParticipantIndexService>();

// Register advertisement resync background service (FR-003, FR-004)
builder.Services.AddHostedService<AdvertisementResyncService>();

// Register event bridge: subscribes to domain events and broadcasts via SignalR
builder.Services.AddHostedService<RegisterEventBridgeService>();

// Feature 047: Local address bloom filter index (US1) + inbound transaction router (US2)
builder.Services.AddGrpc();
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Interfaces.ILocalAddressIndex,
    Sorcha.Register.Service.Services.Implementation.RedisBloomFilterAddressIndex>();
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Interfaces.IInboundTransactionRouter,
    Sorcha.Register.Service.Services.Implementation.InboundTransactionRouter>();
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Interfaces.IBloomFilterRebuilder,
    Sorcha.Register.Service.Services.Implementation.BloomFilterRebuilder>();

// Feature 106 startup-rebuild: reconcile bloom filters on boot in case Redis was wiped
// or hooks failed during normal wallet creation. Non-blocking — runs as a hosted service
// on a startup delay and never blocks ASP.NET startup.
builder.Services.AddHostedService<Sorcha.Register.Service.Services.Implementation.BloomFilterStartupRebuildService>();

// Feature 047: Inbound routing metrics (T047 — observability)
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Implementation.InboundRoutingMetrics>();

// Feature 047: Register recovery service (US4) — detects docket gaps and recovers from peers
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Implementation.RegisterRecoveryService>();
builder.Services.AddSingleton<Sorcha.Register.Service.Services.Interfaces.IRegisterRecoveryService>(sp =>
    sp.GetRequiredService<Sorcha.Register.Service.Services.Implementation.RegisterRecoveryService>());
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<Sorcha.Register.Service.Services.Implementation.RegisterRecoveryService>());

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddRegisterAuthorization();

var app = builder.Build();

if (storageType.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("Register Service using MongoDB storage");
}
else
{
    app.Logger.LogInformation("Register Service using InMemory storage (development mode)");
}

// Map default endpoints (health checks)
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Register Service");

// Map SignalR hub via MapSorchaHubs from the AddSorchaHub registry (Feature 118 US1).
app.MapSorchaHubs();

// Feature 047: Map RegisterAddress gRPC service for bloom filter operations
app.MapGrpcService<Sorcha.Register.Service.GrpcServices.RegisterAddressGrpcService>();

// Feature 047: Map recovery health endpoints (US4)
app.MapRecoveryHealthEndpoints();

// Feature 048: System register query and blueprint endpoints
app.MapSystemRegisterEndpoints();

// Feature 048: Map register policy endpoints (US1)
app.MapRegisterPolicyEndpoints();

// Feature 048: Map validator query endpoints (US3)
app.MapValidatorQueryEndpoints();

// Feature 108: local relationship + sync-state + my-validated-registers endpoints
app.MapRelationshipEndpoints();
app.MapObservationEndpoints();

// T027-T042: Inclusion proofs, revocation, verification bundles
app.MapVerificationEndpoints();

// Feature 188: provenance spine + per-docket trail. Two endpoints deliberately — the spine runs
// NO verification (plan D6).
app.MapProvenanceEndpoints();

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// ===========================
// Register Management API
// ===========================

// Internal discovery endpoint for service-to-service recovery (no auth)
app.MapGet("/api/internal/registers", async (RegisterManager manager) =>
{
    // Status is serialised as the enum name (string) — the consumer-side
    // InternalRegisterInfo.Status is typed string. Without the explicit
    // ToString(), default System.Text.Json emits the underlying int and the
    // client throws JsonException, which the catch-all returns as [],
    // silently breaking bloom-filter fan-out for new wallet addresses.
    var allRegisters = await manager.GetAllRegistersAsync();
    return Results.Ok(allRegisters.Select(r => new
    {
        r.Id,
        r.Name,
        r.Height,
        Status = r.Status.ToString()
    }).ToList());
})
.WithName("InternalGetRegisters")
.WithSummary("Internal: List all registers for service recovery")
.WithDescription("Internal endpoint for Blueprint Service startup recovery. Returns minimal register info. Requires service token.")
.RequireAuthorization("RequireService")
.ExcludeFromDescription(); // Hidden from public OpenAPI docs

// Internal endpoint for Tenant Service subscription notifications
app.MapPost("/api/internal/register-subscriptions", async (
    SubscriptionNotificationRequest request,
    RegisterManager manager,
    IPeerServiceClient peerClient,
    IServiceScopeFactory scopeFactory,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.RegisterId))
    {
        return Results.BadRequest(new { error = "registerId is required" });
    }

    if (string.IsNullOrWhiteSpace(request.Action)
        || (request.Action != "subscribe" && request.Action != "unsubscribe"))
    {
        return Results.BadRequest(new { error = "action must be 'subscribe' or 'unsubscribe'" });
    }

    if (request.Action == "subscribe")
    {
        // Check if register already exists locally
        var existing = await manager.GetRegisterAsync(request.RegisterId);
        if (existing != null)
        {
            logger.LogInformation(
                "Register {RegisterId} already exists locally (SyncState={SyncState}), skipping stub creation",
                request.RegisterId, existing.SyncState);
            return Results.Ok(new SubscriptionNotificationResponse
            {
                RegisterId = request.RegisterId,
                Action = "subscribe",
                SyncState = existing.SyncState?.ToString(),
                Message = existing.SyncState == null
                    ? "Register exists locally"
                    : $"Register already syncing (state: {existing.SyncState})"
            });
        }

        // Create stub register with Checking status (subscription just created, connecting to peers)
        var name = !string.IsNullOrWhiteSpace(request.RegisterName) ? request.RegisterName : "Syncing...";
        var stub = await manager.CreateRegisterAsync(
            name,
            advertise: false,
            isFullReplica: false,
            registerId: request.RegisterId,
            description: request.Description,
            syncState: Sorcha.Register.Models.Enums.RegisterSyncState.Syncing);

        // Set initial status to Checking (connecting to source peers)
        await manager.UpdateRegisterStatusAsync(request.RegisterId, RegisterStatus.Checking);

        logger.LogInformation(
            "Created stub register {RegisterId} (name={Name}) with SyncState=Subscribing",
            request.RegisterId, name);

        // Fire-and-forget: tell Peer Service to start syncing.
        // Uses IServiceScopeFactory to avoid capturing request-scoped RegisterManager
        // which would risk ObjectDisposedException after the HTTP request completes.
        var registerId = request.RegisterId;
        _ = Task.Run(async () =>
        {
            try
            {
                await peerClient.SubscribeToRegisterAsync(registerId, "full-replica");
                await using var scope = scopeFactory.CreateAsyncScope();
                var scopedManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                await scopedManager.UpdateSyncStateAsync(registerId, Sorcha.Register.Models.Enums.RegisterSyncState.Syncing);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to initiate peer sync for register {RegisterId} — setting Error state",
                    registerId);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var scopedManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
                    await scopedManager.UpdateSyncStateAsync(registerId, Sorcha.Register.Models.Enums.RegisterSyncState.Error);
                }
                catch (Exception innerEx)
                {
                    logger.LogError(innerEx,
                        "Failed to set Error sync state for register {RegisterId}",
                        registerId);
                }
            }
        });

        return Results.Ok(new SubscriptionNotificationResponse
        {
            RegisterId = request.RegisterId,
            Action = "subscribe",
            SyncState = "Subscribing",
            Message = "Stub register created, peer sync initiated"
        });
    }
    else // unsubscribe
    {
        var existing = await manager.GetRegisterAsync(request.RegisterId);
        if (existing == null)
        {
            return Results.Ok(new SubscriptionNotificationResponse
            {
                RegisterId = request.RegisterId,
                Action = "unsubscribe",
                Message = "Register not found locally (already removed)"
            });
        }

        if (existing.SyncState == null)
        {
            // Locally owned register — do NOT delete
            logger.LogInformation(
                "Register {RegisterId} is locally owned (SyncState=null), not deleting",
                request.RegisterId);
            return Results.Ok(new SubscriptionNotificationResponse
            {
                RegisterId = request.RegisterId,
                Action = "unsubscribe",
                Message = "Register is locally owned, not removed"
            });
        }

        // Remote register — stop peer sync first, then delete local stub.
        // Note: UnsubscribeFromRegisterAsync swallows exceptions internally and logs warnings.
        await peerClient.UnsubscribeFromRegisterAsync(request.RegisterId);

        await manager.DeleteRemoteRegisterAsync(request.RegisterId);

        logger.LogInformation(
            "Deleted remote register {RegisterId} and stopped peer sync",
            request.RegisterId);

        return Results.Ok(new SubscriptionNotificationResponse
        {
            RegisterId = request.RegisterId,
            Action = "unsubscribe",
            Message = "Register removed and peer sync stopped"
        });
    }
})
.WithName("InternalNotifyRegisterSubscription")
.WithSummary("Internal: Notify of register subscription change")
.WithDescription("Called by Tenant Service when an org subscribes/unsubscribes. Creates stub registers and triggers peer sync.")
.RequireAuthorization("RequireService")
.ExcludeFromDescription();

// Internal endpoint: Peer Service reports sync state changes for a register.
// Maps peer sync state to RegisterStatus and updates both SyncState and Status.
app.MapPost("/api/internal/register-sync-status", async (
    SyncStatusReport report,
    RegisterManager manager,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(report.RegisterId))
        return Results.BadRequest(new { error = "registerId is required" });

    var register = await manager.GetRegisterAsync(report.RegisterId);
    if (register == null)
        return Results.NotFound(new { error = $"Register '{report.RegisterId}' not found" });

    // Map peer sync state to RegisterStatus. A replica pulling chain data is CHECKING, not in
    // RECOVERY — "Recovery" reads as data loss, and this is the ordinary path every joining node
    // takes. Recovery/Offline stay reserved for a register that is actually unwell.
    var newStatus = report.SyncState switch
    {
        "Subscribing" or "Syncing" => RegisterStatus.Checking,
        "FullyReplicated" or "Active" => RegisterStatus.Online,
        "Error" => RegisterStatus.Offline,
        _ when !report.PeerConnectionActive => RegisterStatus.Offline,
        _ => register.Status
    };

    // Map peer's wire string to the typed RegisterSyncState on the register entity (Feature 108).
    var mappedSyncState = report.SyncState switch
    {
        "Subscribing" or "Syncing" => Sorcha.Register.Models.Enums.RegisterSyncState.Syncing,
        "FullyReplicated" or "Active" => Sorcha.Register.Models.Enums.RegisterSyncState.CaughtUp,
        "Error" => Sorcha.Register.Models.Enums.RegisterSyncState.Error,
        _ => Sorcha.Register.Models.Enums.RegisterSyncState.Indeterminate
    };

    if (register.SyncState != mappedSyncState)
    {
        await manager.UpdateSyncStateAsync(report.RegisterId, mappedSyncState);
    }

    if (register.Status != newStatus)
    {
        await manager.UpdateRegisterStatusAsync(report.RegisterId, newStatus);
        logger.LogInformation(
            "Register {RegisterId} status updated: {OldStatus} → {NewStatus} (sync: {SyncState})",
            report.RegisterId, register.Status, newStatus, report.SyncState);
    }

    return Results.Ok(new { registerId = report.RegisterId, status = newStatus.ToString(), syncState = mappedSyncState.ToString() });
})
.WithName("InternalReportSyncStatus")
.WithSummary("Internal: Report peer sync status change")
.WithDescription("Called by Peer Service when sync state changes. Maps sync state to RegisterStatus.")
.RequireAuthorization("RequireService")
.ExcludeFromDescription();

// Group-level policy is the minimal "authenticated" bar so that read endpoints
// (GET /, GET /{id}, GET /stats/count) are accessible to any member — including
// Consumers in the Public org who need to discover registers their org is subscribed
// to. Write endpoints in this group (POST disable-dev-mode, PUT /{id}, DELETE /{id})
// individually require the stricter "CanManageRegisters" policy below.
var registersGroup = app.MapGroup("/api/registers")
    .WithTags("Registers")
    .RequireAuthorization("RequireAuthenticated");

// Disable dev mode (one-way — enables mandatory field-level encryption).
// Emits a CryptoPolicyUpdate control transaction (DevMode=false) rather than flipping a local
// flag, so the promotion is sealed into the chain, passes the validator's one-way guard, and
// REPLICATES to every node (each projects it onto its register record when the docket finalises).
// A local-only flip would desync the owner from its replicas — the exact class of bug this avoids.
registersGroup.MapPost("/{registerId}/disable-dev-mode", async (
    string registerId,
    RegisterManager manager,
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    CancellationToken ct) =>
{
    var register = await manager.GetRegisterAsync(registerId, ct);
    if (register is null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }
    if (!register.DevMode)
    {
        return Results.Conflict(new { error = "Dev mode is already disabled on this register." });
    }

    // Base the new policy on the current active one, flipping DevMode off and bumping the version.
    var activePolicy = await cryptoPolicyService.GetActivePolicyAsync(registerId, ct);
    activePolicy.DevMode = false;
    activePolicy.Version += 1;
    activePolicy.EffectiveFrom = DateTime.UtcNow;

    try
    {
        var submitted = await cryptoPolicyService.SubmitPolicyUpdateAsync(
            registerId, activePolicy, updatedBy: "disable-dev-mode", ct);

        return Results.Ok(new
        {
            registerId,
            txId = submitted.TransactionId,
            policyVersion = submitted.PolicyVersion,
            status = "submitted",
            message = "Dev mode disable submitted as a crypto-policy update. Field-level encryption " +
                      "becomes mandatory once the control transaction seals; the change replicates to all nodes."
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(
            title: "Crypto policy update rejected",
            detail: ex.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }
})
.WithName("DisableDevMode")
.WithSummary("Disable dev mode (one-way)")
.WithDescription("Irreversibly disables dev mode via a replicated crypto-policy update, enabling mandatory field-level encryption for new transactions. Cannot be undone (validators reject re-enabling DevMode).")
.RequireAuthorization("CanManageRegisters");

// NOTE: POST /api/registers/ (simple CRUD creation) has been removed.
// All register creation must go through the two-phase initiate/finalize flow.
// See register creation endpoints below (POST /api/registers/initiate and POST /api/registers/finalize).

// <summary>
// Get all registers
// </summary>
registersGroup.MapGet("/", async (
    RegisterManager manager,
    HttpContext httpContext) =>
{
    var orgIdClaim = httpContext.User.FindFirst("org_id")?.Value;
    if (string.IsNullOrEmpty(orgIdClaim) || !Guid.TryParse(orgIdClaim, out var orgId))
    {
        // No org context — return only system registers
        var allRegisters = await manager.GetAllRegistersAsync();
        return Results.Ok(allRegisters.Where(r => r.Purpose == Sorcha.Register.Models.Enums.RegisterPurpose.System));
    }

    var registers = await manager.GetRegistersForOrgAsync(orgId);
    return Results.Ok(registers);
})
.WithName("GetAllRegisters")
.WithSummary("Get accessible registers")
.WithDescription("Returns registers the caller's organisation is subscribed to, plus all system registers.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get register by ID
// </summary>
// <remarks>
// Returns the full register record. Feature 142 surfaces two fields used by the
// Go-live detail card and the Go-live target picker:
//   - <c>advertise</c> — register visibility (public/private to the peer network).
//   - <c>sandbox</c> — computed; true when the register is a rehearsal sandbox
//     (control-record metadata "sandbox" == "true"). Callers exclude sandbox
//     registers from Go-live targets and normal listings.
// </remarks>
// Feature 175: single register-read path, credential-gated rather than a parallel /api/public
// namespace. Anonymous callers may read a register ONLY when it is public (Advertise==true) — the
// credential (or its absence + the public flag) gates the read. Authenticated callers read as before.
// A non-public register is refused 403 to an anonymous caller (never disclosed). Rate-limited
// (burst-tolerant) since it is now anonymously reachable — a full-register federation pull must not throttle.
registersGroup.MapGet("/{id}", async (
    RegisterManager manager,
    HttpContext httpContext,
    string id) =>
{
    var register = await manager.GetRegisterAsync(id);
    if (register is null)
        return Results.NotFound();

    var isAuthenticated = httpContext.User?.Identity?.IsAuthenticated ?? false;
    if (!isAuthenticated && !register.Advertise)
        return Results.Json(new { error = "Register is not public" }, statusCode: StatusCodes.Status403Forbidden);

    return Results.Ok(register);
})
.WithName("GetRegister")
.WithSummary("Get register by ID")
.WithDescription("Retrieves a register by id. Authenticated callers read any register they can access; anonymous callers (e.g. a foreign node bootstrapping federation) read a register only when it is public (advertised) — a non-public register is refused 403. Surfaces 'advertise' (visibility) and a computed 'sandbox' flag (Feature 142).")
.AllowAnonymous()
.RequireRateLimiting(RateLimitPolicies.Relaxed)
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status404NotFound);

// <summary>
// Update register
// </summary>
registersGroup.MapPut("/{id}", async (
    RegisterManager manager,
    IPeerServiceClient peerClient,
    ILogger<Program> logger,
    string id,
    UpdateRegisterRequest request) =>
{
    var register = await manager.GetRegisterAsync(id);
    if (register is null)
        return Results.NotFound();

    var advertiseChanged = request.Advertise is not null && register.Advertise != request.Advertise.Value;

    if (request.Name is not null)
        register.Name = request.Name;
    if (request.Status is not null)
        register.Status = request.Status.Value;
    if (request.Advertise is not null)
        register.Advertise = request.Advertise.Value;

    var updated = await manager.UpdateRegisterAsync(register);

    // Notify Peer Service when advertise flag changes (fire-and-forget)
    if (advertiseChanged)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await peerClient.AdvertiseRegisterAsync(
                    register.Id, register.Advertise,
                    name: register.Name, description: register.Description);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to notify Peer Service about advertise change for register {RegisterId}",
                    register.Id);
            }
        });
    }

    return Results.Ok(updated);
})
.WithRequestValidation()
.WithName("UpdateRegister")
.WithSummary("Update register")
.WithDescription("Updates register metadata and settings.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized)
.RequireAuthorization("CanManageRegisters");

// <summary>
// Delete register
// </summary>
registersGroup.MapDelete("/{id}", async (
    RegisterManager manager,
    IRegisterRepository registerRepository,
    string id,
    HttpContext httpContext) =>
{
    try
    {
        var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;

        // Load control record attestations from genesis transaction (docket 0)
        var genesisTxs = await registerRepository.GetTransactionsByDocketAsync(id, 0, httpContext.RequestAborted);
        var genesisTx = genesisTxs.FirstOrDefault();
        var attestations = new List<RegisterAttestation>();

        if (genesisTx?.Payloads is { Length: > 0 })
        {
            try
            {
                var payloadData = genesisTx.Payloads[0].Data;
                if (!string.IsNullOrWhiteSpace(payloadData))
                {
                    var payloadBytes = payloadData.Contains('+') || payloadData.Contains('/') || payloadData.Contains('=')
                        ? Convert.FromBase64String(payloadData)
                        : System.Buffers.Text.Base64Url.DecodeFromChars(payloadData);
                    var controlPayload = System.Text.Json.JsonSerializer.Deserialize<ControlTransactionPayload>(
                        payloadBytes, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (controlPayload?.Roster?.Attestations != null)
                    {
                        attestations = controlPayload.Roster.Attestations;
                    }
                }
            }
            catch
            {
                // Failed to deserialize — attestations list stays empty, caught by manager guard
            }
        }

        await manager.DeleteRegisterAsync(id, walletAddress, attestations.AsReadOnly(), httpContext.RequestAborted);
        // SignalR notification handled by RegisterEventBridgeService via RegisterDeletedEvent
        return Results.NoContent();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Problem(title: "Forbidden", detail: "You are not authorized to delete this register.", statusCode: 403);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("no attestations") || ex.Message.Contains("data corruption"))
    {
        return Results.Problem(title: "Data integrity error", detail: "Control record is missing or corrupted.", statusCode: 500);
    }
})
.WithName("DeleteRegister")
.WithSummary("Delete register")
.WithDescription("Deletes a register. Authorization is based on control record attestations. System registers cannot be deleted.")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized)
.RequireAuthorization("CanManageRegisters");

// <summary>
// Get register count
// </summary>
registersGroup.MapGet("/stats/count", async (RegisterManager manager) =>
{
    var count = await manager.GetRegisterCountAsync();
    return Results.Ok(new { count });
})
.WithName("GetRegisterCount")
.WithSummary("Get register count")
.WithDescription("Returns the total number of registers.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Register Creation with Genesis Transactions (FR-REG-001A)
// ===========================
// Separate endpoint group for register creation workflow (initiate/finalize)
// Requires authenticated user with org admin role (CanManageRegisters policy)
var registerCreationGroup = app.MapGroup("/api/registers")
    .WithTags("Register Creation")
    .RequireAuthorization("CanManageRegisters");

// <summary>
// Initiate register creation (Phase 1): Generate unsigned control record
// </summary>
registerCreationGroup.MapPost("/initiate", async (
    IRegisterCreationOrchestrator orchestrator,
    InitiateRegisterCreationRequest request,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    try
    {
        // Enforce CanCreateSystemRegisters policy for System purpose
        if (request.Purpose == Sorcha.Register.Models.Enums.RegisterPurpose.System)
        {
            var orgId = httpContext.User.FindFirst("org_id")?.Value;
            var isSystemAdminOrg = orgId == "00000000-0000-0000-0000-000000000001";
            var isSystemAdmin = httpContext.User.IsInRole("SystemAdmin");
            if (!isSystemAdminOrg || !isSystemAdmin)
            {
                return Results.Problem(
                    title: "Forbidden",
                    detail: "Only system administrators can create system registers.",
                    statusCode: 403);
            }
        }

        var response = await orchestrator.InitiateAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message, details = "Invalid request parameters" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Register initiation failed",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("InitiateRegisterCreation")
.WithSummary("Initiate register creation (Phase 1)")
.WithDescription(@"
**Phase 1: Generate Unsigned Control Record**

Initiates the two-phase register creation workflow by generating a unique register ID
and unsigned control record template. The client must sign the returned `dataToSign` hash
with each admin's wallet before calling the finalize endpoint.

**Workflow:**
1. Server generates unique register ID and control record template
2. Server computes SHA-256 hash of control record for signing
3. Client signs the hash with each admin's wallet (offline/client-side)
4. Client calls /finalize with signed control record

**Control Record:**
The control record establishes administrative control with cryptographic attestations.
At least one 'owner' attestation is required.

**Expiration:**
The pending registration expires after 5 minutes. The client must finalize within this timeframe.

**Returns:**
- `registerId`: Generated unique ID
- `controlRecord`: Template with placeholder signatures
- `dataToSign`: SHA-256 hash to sign with wallets
- `expiresAt`: Expiration timestamp
- `nonce`: Replay protection nonce
")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem();

// <summary>
// Finalize register creation (Phase 2): Verify signatures and create register
// </summary>
registerCreationGroup.MapPost("/finalize", async (
    IRegisterCreationOrchestrator orchestrator,
    FinalizeRegisterCreationRequest request,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    try
    {
        // Pull the caller's org_id and sub claims so the orchestrator can
        // create an owner subscription via the Tenant Service internal
        // endpoint. These are read from the JWT, NEVER from the request body —
        // that's the whole point: the caller doesn't get to pick whose org
        // becomes the owner.
        var orgIdClaim = httpContext.User.FindFirst("org_id")?.Value
            ?? httpContext.User.FindFirst("organization_id")?.Value;
        var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value;

        Guid.TryParse(orgIdClaim, out var callerOrgId);
        Guid.TryParse(userIdClaim, out var callerUserId);

        var response = await orchestrator.FinalizeAsync(
            request,
            callerOrgId,
            callerUserId,
            cancellationToken);
        return Results.Created($"/api/registers/{response.RegisterId}", response);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("expired"))
    {
        return Results.Problem(
            title: "Registration expired",
            detail: ex.Message,
            statusCode: 408); // Request Timeout
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(
            title: "Signature verification failed",
            detail: ex.Message,
            statusCode: 401); // Unauthorized
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message, details = "Invalid control record or signatures" });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Register finalization failed",
            detail: ex.Message,
            statusCode: 500);
    }
})
.WithName("FinalizeRegisterCreation")
.WithSummary("Finalize register creation (Phase 2)")
.WithDescription(@"
**Phase 2: Verify Signatures and Create Register**

Completes the register creation workflow by verifying all attestation signatures,
creating the register in the database, and generating the genesis transaction.

**Workflow:**
1. Server retrieves pending registration by ID and nonce
2. Server validates control record against JSON Schema
3. Server verifies each attestation signature using public keys
4. Server creates register in database
5. Server creates genesis transaction with control record payload
6. Server submits genesis transaction to Validator Service
7. Validator creates genesis docket (height 0)

**Signature Verification:**
- Each attestation signature is verified using the subject's public key
- Supported algorithms: ED25519, NISTP256, RSA4096
- Signature must match the SHA-256 hash from initiation phase

**Genesis Transaction:**
The genesis transaction contains the signed control record and establishes
an immutable audit trail of register creation and ownership.

**Returns:**
- `registerId`: Created register ID
- `status`: 'created'
- `genesisTransactionId`: Genesis transaction ID
- `genesisDocketId`: '0' (genesis docket)
- `createdAt`: Creation timestamp

**Errors:**
- 400 Bad Request: Invalid control record or validation errors
- 401 Unauthorized: Signature verification failed
- 408 Request Timeout: Pending registration expired
- 500 Internal Server Error: Database or service error
")
.Produces<object>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem();

// ===========================
// Transaction Management API
// ===========================

var transactionsGroup = app.MapGroup("/api/registers/{registerId}/transactions")
    .WithTags("Transactions");

// <summary>
// Submit a transaction
// </summary>
transactionsGroup.MapPost("/", async (
    TransactionManager manager,
    IEventPublisher eventPublisher,
    string registerId,
    TransactionModel transaction) =>
{
    try
    {
        transaction.RegisterId = registerId;
        var stored = await manager.StoreTransactionAsync(transaction);

        // Publish event — SignalR notification handled by RegisterEventBridgeService
        await eventPublisher.PublishAsync(
            "transaction:confirmed",
            new TransactionConfirmedEvent
            {
                TransactionId = stored.TxId,
                RegisterId = registerId,
                ToWallets = stored.RecipientsWallets?.ToList() ?? [],
                SenderWallet = stored.SenderWallet,
                PreviousTransactionId = stored.PrevTxId,
                MetaData = stored.MetaData,
                ConfirmedAt = DateTime.UtcNow
            });

        return Results.Created($"/api/registers/{registerId}/transactions/{stored.TxId}", stored);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
.WithName("SubmitTransaction")
.WithSummary("Submit a transaction (internal/diagnostic only)")
.WithDescription("Stores a transaction directly in the register. Action transactions should be submitted via the Validator Service pipeline.")
.RequireAuthorization("CanWriteDockets")
.Produces<TransactionModel>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get transaction by ID
// </summary>
transactionsGroup.MapGet("/{txId}", async (
    TransactionManager manager,
    string registerId,
    string txId) =>
{
    var transaction = await manager.GetTransactionAsync(registerId, txId);
    return transaction is not null ? Results.Ok(transaction) : Results.NotFound();
})
.WithName("GetTransaction")
.WithSummary("Get transaction by ID")
.WithDescription("Retrieves a specific transaction by its ID.")
.RequireAuthorization("CanReadTransactions")
.Produces<TransactionModel>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get all transactions for a register (queryable)
// </summary>
transactionsGroup.MapGet("/", async (
    TransactionManager manager,
    string registerId,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$skip")] int? skip,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$top")] int? top,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$count")] bool? count) =>
{
    var odataSkip = skip ?? 0;
    var odataTop = top ?? 20;

    // Pushed down to the store: count + newest-first page, index-backed (TimeStamp desc), rather
    // than materialising the whole ledger to count and page in memory.
    var totalCount = await manager.CountTransactionsAsync(registerId);
    var paged = await manager.GetLatestTransactionsAsync(registerId, odataSkip, odataTop);

    // OData-style paged response
    var page = odataTop > 0 ? (odataSkip / odataTop) + 1 : 1;
    return Results.Ok(new
    {
        Page = page,
        PageSize = odataTop,
        Total = totalCount,
        Transactions = paged
    });
})
.WithName("GetTransactions")
.WithSummary("Get all transactions")
.WithDescription("Retrieves all transactions for a register with OData pagination ($skip, $top, $count).")
.RequireAuthorization("CanReadTransactions")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get lightweight transaction graph for DAG visualization
// </summary>
transactionsGroup.MapGet("/graph", async (
    IRegisterRepository repository,
    string registerId,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? limit,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? before) =>
{
    // Validate limit parameter
    var effectiveLimit = limit ?? 200;
    if (effectiveLimit < 1 || effectiveLimit > 1000)
    {
        return Results.BadRequest(new { error = "limit must be between 1 and 1000" });
    }

    // Verify register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register is null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    // Cursor-based pagination pushed down to the store: resolve the cursor tx by id, then a single
    // index-backed page of transactions older than it (newest-first) + a count for totalCount/hasMore.
    // No cursor (or an unknown cursor) → the latest page. Never materialises the whole ledger.
    long totalCount;
    IReadOnlyList<TransactionModel> pageTxs;
    if (!string.IsNullOrEmpty(before)
        && await repository.GetTransactionAsync(registerId, before) is { } cursorTx)
    {
        totalCount = await repository.CountTransactionsBeforeAsync(registerId, cursorTx.TimeStamp);
        pageTxs = await repository.GetTransactionsBeforeAsync(registerId, cursorTx.TimeStamp, effectiveLimit);
    }
    else
    {
        totalCount = await repository.CountTransactionsAsync(registerId);
        pageTxs = await repository.GetLatestTransactionsAsync(registerId, 0, effectiveLimit);
    }

    var nodes = pageTxs
        .Select(t => new TransactionGraphNodeDto(
            t.TxId,
            t.PrevTxId,
            t.SenderWallet,
            t.TimeStamp,
            t.DocketNumber,
            t.MetaData?.BlueprintId,
            t.MetaData?.InstanceId,
            t.MetaData is not null ? (int?)t.MetaData.TransactionType : null))
        .ToArray();

    return Results.Ok(new TransactionGraphResponse(
        registerId,
        nodes,
        (int)totalCount,
        totalCount > nodes.Length));
})
.WithName("GetTransactionGraph")
.WithSummary("Get lightweight transaction graph for DAG visualization")
.WithDescription("Returns transaction IDs and PrevTxId links without payload data. Used by the Register Map UI for building the transaction lineage DAG.")
.WithTags("Query")
.RequireAuthorization("CanReadTransactions")
.Produces<TransactionGraphResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Query API
// ===========================

var queryGroup = app.MapGroup("/api/query")
    .WithTags("Query")
    .RequireAuthorization("CanReadTransactions");

// <summary>
// Query transactions by wallet address
// </summary>
queryGroup.MapGet("/wallets/{address}/transactions", async (
    QueryManager manager,
    string address,
    string? registerId = null,
    int page = 1,
    int pageSize = 20) =>
{
    if (registerId is not null)
    {
        var result = await manager.GetTransactionsByWalletPaginatedAsync(
            registerId,
            address,
            page,
            pageSize);
        return Results.Ok(result);
    }

    // Cross-register wallet query
    var crossResult = await manager.GetTransactionsByWalletAcrossRegistersAsync(
        address, page, pageSize);
    return Results.Ok(crossResult);
})
.WithName("GetTransactionsByWallet")
.WithSummary("Query transactions by wallet")
.WithDescription("Retrieves all transactions for a specific wallet address.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Query transactions by sender
// </summary>
queryGroup.MapGet("/senders/{address}/transactions", async (
    QueryManager manager,
    string address,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var result = await manager.GetTransactionsByWalletPaginatedAsync(
        registerId,
        address,
        page,
        pageSize,
        asSender: true,
        asRecipient: false);
    return Results.Ok(result);
})
.WithName("GetTransactionsBySender")
.WithSummary("Query transactions by sender")
.WithDescription("Retrieves all transactions sent by a specific address.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Query transactions by blueprint
// </summary>
queryGroup.MapGet("/blueprints/{blueprintId}/transactions", async (
    QueryManager manager,
    string blueprintId,
    string registerId,
    string? instanceId = null) =>
{
    var result = await manager.GetTransactionsByBlueprintAsync(
        registerId,
        blueprintId,
        instanceId);

    return Results.Ok(result);
})
.WithName("GetTransactionsByBlueprint")
.WithSummary("Query transactions by blueprint")
.WithDescription("Retrieves all transactions for a specific blueprint.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get transaction statistics
// </summary>
queryGroup.MapGet("/stats", async (
    QueryManager manager,
    string registerId) =>
{
    var stats = await manager.GetTransactionStatisticsAsync(registerId);
    return Results.Ok(stats);
})
.WithName("GetTransactionStatistics")
.WithSummary("Get transaction statistics")
.WithDescription("Retrieves comprehensive statistics for a register.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Query transactions by previous transaction ID (for fork detection and chain traversal)
// </summary>
queryGroup.MapGet("/previous/{prevTxId}/transactions", async (
    QueryManager manager,
    string prevTxId,
    [Microsoft.AspNetCore.Mvc.FromQuery] string? registerId,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$skip")] int? skip,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$top")] int? top,
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "$count")] bool? count) =>
{
    if (registerId is null)
    {
        return Results.BadRequest(new { error = "registerId is required" });
    }

    var odataSkip = skip ?? 0;
    var odataTop = top ?? 20;
    var page = odataTop > 0 ? (odataSkip / odataTop) + 1 : 1;

    var result = await manager.GetTransactionsByPrevTxIdPaginatedAsync(
        registerId,
        prevTxId,
        page,
        odataTop);
    return Results.Ok(result);
})
.WithName("GetTransactionsByPrevTxId")
.WithSummary("Query transactions by previous transaction ID")
.WithDescription("Retrieves all transactions that reference a given previous transaction ID. Used for fork detection and chain integrity auditing.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Query transactions by workflow instance ID. Used by the Validator's Tier 3
// chain-derived participant binding (PR #324) to find the earliest prior in-instance
// tx for a participant role. Ordered by TimeStamp ascending so the caller can
// short-circuit on the first match.
// </summary>
queryGroup.MapGet("/instance/{instanceId}/transactions/{registerId}", async (
    TransactionManager manager,
    string instanceId,
    string registerId) =>
{
    if (string.IsNullOrWhiteSpace(instanceId))
        return Results.BadRequest(new { error = "instanceId is required" });
    if (string.IsNullOrWhiteSpace(registerId))
        return Results.BadRequest(new { error = "registerId is required" });

    var txs = (await manager.GetTransactionsByInstanceAsync(registerId, instanceId))
        .OrderBy(t => t.TimeStamp)
        .ToList();
    return Results.Ok(txs);
})
.WithName("GetTransactionsByInstanceId")
.WithSummary("Query transactions by workflow instance ID")
.WithDescription("Returns all transactions for a workflow instance on the given register, ordered by timestamp. Powers the Validator's chain-derived participant binding.")
.Produces<List<Sorcha.Register.Models.TransactionModel>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// DocketHeader Management API
// ===========================

var docketsGroup = app.MapGroup("/api/registers/{registerId}/dockets")
    .WithTags("Dockets")
    .RequireAuthorization("CanReadTransactions");

// Feature 175: single docket-read path, credential-gated (rather than a parallel /api/public
// namespace). Anonymous callers may read a PUBLIC (advertised) register's dockets — federation
// bootstrap — while private-register docket reads still require authentication (CanReadTransactions
// is RequireAuthenticatedUser, so anonymous-or-public is a proper superset, no weakening). Returns a
// refusal IResult for a disallowed anonymous read, or null to proceed. Authenticated callers proceed.
static async Task<IResult?> DocketAnonymousReadRefusalAsync(
    IRegisterRepository repository, HttpContext httpContext, string registerId)
{
    if (httpContext.User?.Identity?.IsAuthenticated ?? false)
        return null;
    var register = await repository.GetRegisterAsync(registerId);
    if (register is null)
        return Results.NotFound(new { error = "Register not found" });
    if (!register.Advertise)
        return Results.Json(new { error = "Register is not public" }, statusCode: StatusCodes.Status403Forbidden);
    return null;
}

// <summary>
// Get all dockets for a register
// </summary>
docketsGroup.MapGet("/", async (
    IRegisterRepository repository,
    HttpContext httpContext,
    string registerId) =>
{
    var refusal = await DocketAnonymousReadRefusalAsync(repository, httpContext, registerId);
    if (refusal is not null) return refusal;
    var dockets = await repository.GetDocketsAsync(registerId);
    return Results.Ok(dockets);
})
.WithName("GetDockets")
.WithSummary("Get all dockets")
.WithDescription("Retrieves all dockets for a register. Anonymous when the register is public (advertised); a non-public register requires authentication (403 to anonymous).")
.AllowAnonymous()
.RequireRateLimiting(RateLimitPolicies.Relaxed)
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status403Forbidden);

// <summary>
// Get docket by ID
// </summary>
docketsGroup.MapGet("/{docketId}", async (
    IRegisterRepository repository,
    HttpContext httpContext,
    string registerId,
    ulong docketId) =>
{
    var refusal = await DocketAnonymousReadRefusalAsync(repository, httpContext, registerId);
    if (refusal is not null) return refusal;
    var docket = await repository.GetDocketAsync(registerId, docketId);
    return docket is not null ? Results.Ok(docket) : Results.NotFound();
})
.WithName("GetDocket")
.WithSummary("Get docket by ID")
.WithDescription("Retrieves a specific docket by its ID (docket height). Anonymous when the register is public (advertised); a non-public register requires authentication (403 to anonymous).")
.AllowAnonymous()
.RequireRateLimiting(RateLimitPolicies.Relaxed)
.Produces<DocketHeader>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status404NotFound);

// <summary>
// Get transactions in a docket
// </summary>
docketsGroup.MapGet("/{docketId}/transactions", async (
    IRegisterRepository repository,
    string registerId,
    ulong docketId) =>
{
    var transactions = await repository.GetTransactionsByDocketAsync(registerId, docketId);
    return Results.Ok(transactions);
})
.WithName("GetDocketTransactions")
.WithSummary("Get docket transactions")
.WithDescription("Retrieves all transactions sealed in a specific docket.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get the latest docket for a register
// </summary>
docketsGroup.MapGet("/latest", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    if (register.Height == 0)
    {
        return Results.Ok<DocketHeader?>(null);
    }

    // Height is count-based (1 = genesis docket written, 2 = two dockets, etc.)
    // Latest docket ID = Height - 1
    var docket = await repository.GetDocketAsync(registerId, (ulong)(register.Height - 1));
    return docket is not null ? Results.Ok(docket) : Results.NotFound();
})
.WithName("GetLatestDocket")
.WithSummary("Get latest docket")
.WithDescription("Retrieves the most recent docket (block) for a register.")
.Produces<DocketHeader>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// Feature 175 follow-up: the parallel /api/public/registers namespace has been removed — anonymous
// public read is now folded into the canonical register + docket read endpoints above (credential-
// gated on the per-request Advertise flag), so a public register has ONE URI whether read
// anonymously or authenticated. Writes and private registers keep their existing auth.

// <summary>
// Write a confirmed docket to the register (Validator Service only)
// </summary>
docketsGroup.MapPost("/", async (
    IRegisterRepository repository,
    Sorcha.Register.Core.Events.IEventPublisher eventPublisher,
    Sorcha.Register.Service.Services.Interfaces.IInboundTransactionRouter transactionRouter,
    Sorcha.Register.Core.Managers.RegisterManager registerManager,
    Sorcha.Register.Service.Services.RelationshipChangeNotifier relationshipNotifier,
    ILogger<Program> logger,
    string registerId,
    WriteDocketRequest request) =>
{
    // Validate register exists. A genesis docket (DocketNumber 0) for a register not
    // yet known locally is the create-on-sync path: a peer is replicating a register's
    // genesis to this node. By the time the docket reaches here the peer's
    // DocketFinalizationService has already verified chain integrity, docket hash, and
    // proposer signature (and the trust anchor, for the system register), so creating
    // the register from the verified genesis is safe. The node only pulls genesis
    // dockets for registers it has explicitly subscribed to.
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        if (request.DocketNumber == 0 &&
            registerId == Sorcha.Register.Models.Constants.SystemRegisterConstants.SystemRegisterId)
        {
            logger.LogInformation("Auto-creating system register for genesis docket");
            register = await registerManager.CreateRegisterAsync(
                Sorcha.Register.Models.Constants.SystemRegisterConstants.SystemRegisterName,
                advertise: true,
                isFullReplica: true,
                registerId: registerId,
                description: "Sorcha platform system register — root of trust for blueprints and governance.",
                purpose: Sorcha.Register.Models.Enums.RegisterPurpose.System);
        }
        else if (request.DocketNumber == 0)
        {
            // Regular register replicated from a peer: derive name/description from the
            // genesis control record where available, else fall back to a synthetic name
            // so the register row exists and subsequent dockets can be written.
            var controlRecord = Sorcha.Register.Service.Services.GenesisControlRecordExtractor.TryExtract(request.Transactions);

            // Feature 175 (T021) — register-identity binding, fail-closed. A create-on-sync genesis is
            // written into the register slot named by the route; if the synced control record declares a
            // DIFFERENT RegisterId, the peer is serving a genesis for another register into this slot.
            // Reject rather than persist a register whose own control record disagrees with its id. The
            // full genesis-attestation signature re-verification on the sync path is tracked as the
            // Register-service-owns-verification architectural decision (see specs/175 research.md).
            if (controlRecord is { RegisterId.Length: > 0 } &&
                !string.Equals(controlRecord.RegisterId, registerId, StringComparison.Ordinal))
            {
                logger.LogCritical(
                    "[register] REGISTER-IDENTITY MISMATCH on create-on-sync for {RegisterId}: synced genesis " +
                    "control record declares RegisterId {DeclaredRegisterId}. Rejecting the docket write (F175 T021).",
                    registerId, controlRecord.RegisterId);
                return Results.Conflict(new
                {
                    error = "Register identity mismatch",
                    registerId,
                    declaredRegisterId = controlRecord.RegisterId
                });
            }

            var replicaName = controlRecord?.Name is { Length: > 0 } controlName
                ? controlName
                : $"replica-{registerId[..Math.Min(8, registerId.Length)]}";
            // Respect the owner's DevMode posture from the synced genesis crypto policy. A
            // DevMode register stores plaintext payloads, so the replica must know this to read
            // them directly (and to apply the same plaintext-permitting rules). Defaults to
            // false (encrypted) when the control record/policy is absent — fail-safe toward encryption.
            var replicaDevMode = controlRecord?.CryptoPolicy?.DevMode ?? false;
            logger.LogInformation(
                "Auto-creating replicated register {RegisterId} ({Name}) from synced genesis docket (DevMode={DevMode})",
                registerId, replicaName, replicaDevMode);
            register = await registerManager.CreateRegisterAsync(
                replicaName,
                advertise: true,
                isFullReplica: true,
                registerId: registerId,
                description: controlRecord?.Description,
                devMode: replicaDevMode,
                purpose: Sorcha.Register.Models.Enums.RegisterPurpose.General,
                initialControlRecord: controlRecord);
        }
        else
        {
            return Results.NotFound(new { error = "Register not found" });
        }
    }
    else if (request.DocketNumber == 0 && !register.DevMode)
    {
        // Reconcile DevMode from the synced genesis control record even when the register row
        // already existed — e.g. a subscribe-flow stub was created (DevMode defaulting to false)
        // before the genesis docket arrived, so the create-on-sync DevMode extraction above was
        // skipped. The owner's DevMode posture is authoritative; a replica that misses it refuses
        // to read the owner's plaintext payloads, so register-native credential delivery (a
        // DevMode credential carried in an action payload) is silently dropped by the wallet
        // service's InboundCredentialDetector. Only ever turns DevMode ON from genesis — turning
        // it off is the governed one-way crypto-policy-update path, never a replication side effect.
        var genesisControl = Sorcha.Register.Service.Services.GenesisControlRecordExtractor.TryExtract(request.Transactions);
        if (genesisControl?.CryptoPolicy?.DevMode == true)
        {
            register.DevMode = true;
            await registerManager.UpdateRegisterAsync(register);
            logger.LogInformation(
                "Reconciled DevMode=true on replicated register {RegisterId} from synced genesis control record",
                registerId);
        }
    }

    // Create docket from request
    var docket = new DocketHeader
    {
        Id = (ulong)request.DocketNumber,
        RegisterId = registerId,
        PreviousHash = request.PreviousHash ?? string.Empty,
        Hash = request.DocketHash,
        TransactionIds = request.TransactionIds,
        TimeStamp = request.CreatedAt.UtcDateTime,
        State = DocketState.Sealed,
        MetaData = new TransactionMetaData
        {
            RegisterId = registerId
        },
        // Feature 187 (#1371): each of these lands in its OWN field. ProposerValidatorId used to be
        // smuggled through `Votes` (a string? documented "Consensus votes (implementation TBD)"),
        // MerkleRoot was not persisted at all, and real consensus votes never reached the ledger.
        ProposerValidatorId = request.ProposerValidatorId,
        MerkleRoot = request.MerkleRoot,
        Votes = request.Votes ?? new List<Sorcha.Register.Models.ConsensusVote>()
    };

    // Insert transaction documents if provided
    if (request.Transactions is not null && request.Transactions.Any())
    {
        var participantIndex = app.Services.GetRequiredService<ParticipantIndexService>();

        foreach (var tx in request.Transactions)
        {
            // Set docket number for each transaction
            tx.DocketNumber = (ulong)request.DocketNumber;
            logger.LogInformation(
                "[TRACKDIAG register] docket {DocketNumber} tx {TxId}: type={Type} trackingDataCount={Count}",
                request.DocketNumber, tx.TxId, tx.MetaData?.TransactionType,
                tx.MetaData?.TrackingData?.Count ?? -1);
            try
            {
                await repository.InsertTransactionAsync(tx);
            }
            catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
            {
                // A transaction with this TxId already exists. This is legitimate for an idempotent
                // docket write-back (genesis transactions pre-persisted during register creation, or a
                // validator retry after a lost response) — but ONLY when the persisted transaction is
                // the same one. A same-TxId / different-signature collision is a real integrity
                // divergence; masking it as success silently drops the incoming transaction (#814), so
                // verify the signature matches before treating the duplicate as a no-op.
                var existingTx = await repository.GetTransactionAsync(registerId, tx.TxId);
                if (DocketWriteReconciliation.ReconcileTransaction(existingTx, tx)
                    == DocketWriteReconciliation.Verdict.IdempotentMatch)
                {
                    logger.LogDebug(
                        "[register] tx {TxId} already persisted with matching signature — idempotent write-back, re-insert skipped",
                        tx.TxId);
                }
                else
                {
                    logger.LogCritical(
                        "[register] INTEGRITY DIVERGENCE writing docket {DocketNumber} for register {RegisterId}: " +
                        "transaction {TxId} already exists with a different signature " +
                        "(existing={ExistingSignature}, incoming={IncomingSignature}). Rejecting the docket write " +
                        "rather than silently dropping the transaction (#814).",
                        request.DocketNumber, registerId, tx.TxId,
                        existingTx?.Signature ?? "<missing>", tx.Signature);
                    return Results.Conflict(new
                    {
                        error = "Transaction integrity divergence",
                        registerId,
                        docketNumber = request.DocketNumber,
                        txId = tx.TxId,
                    });
                }
            }

            // Index participant transactions for fast address/ID lookups
            if (tx.MetaData?.TransactionType == TransactionType.Participant &&
                tx.Payloads.Length > 0 && !string.IsNullOrEmpty(tx.Payloads[0].Data))
            {
                try
                {
                    var payloadJson = System.Text.Encoding.UTF8.GetString(
                        Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(tx.Payloads[0].Data));
                    var payloadElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payloadJson);
                    participantIndex.IndexParticipant(registerId, tx.TxId, payloadElement, tx.TimeStamp);
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Failed to index participant TX {TxId}", tx.TxId);
                }
            }
        }
    }

    // Insert docket (handle idempotent retries)
    DocketHeader inserted;
    try
    {
        inserted = await repository.InsertDocketAsync(docket);
    }
    catch (MongoDB.Driver.MongoWriteException ex) when (ex.WriteError?.Category == MongoDB.Driver.ServerErrorCategory.DuplicateKey)
    {
        // A docket already occupies this number (the docket number is the Mongo _id). Treat it as a
        // successful idempotent retry ONLY when the persisted docket is the SAME docket — i.e. its
        // hash matches. A same-number / different-hash collision means a divergent docket was built
        // on a stale chain head; masking it as success silently drops this docket's transactions
        // (#814), so verify the hash before returning success and reject on divergence.
        var existingDocket = await repository.GetDocketAsync(registerId, (ulong)request.DocketNumber);
        if (DocketWriteReconciliation.ReconcileDocket(existingDocket, docket)
            == DocketWriteReconciliation.Verdict.IdempotentMatch)
        {
            logger.LogInformation(
                "DocketHeader {DocketNumber} for register {RegisterId} already written with matching hash — idempotent retry, treating as success",
                request.DocketNumber, registerId);
            inserted = existingDocket!;
        }
        else
        {
            logger.LogCritical(
                "[register] INTEGRITY DIVERGENCE writing docket {DocketNumber} for register {RegisterId}: " +
                "a different docket already occupies this number (existing hash={ExistingHash}, incoming hash={IncomingHash}). " +
                "Rejecting rather than silently dropping this docket's transactions (#814).",
                request.DocketNumber, registerId,
                existingDocket?.Hash ?? "<missing>", docket.Hash);
            return Results.Conflict(new
            {
                error = "DocketHeader integrity divergence",
                registerId,
                docketNumber = request.DocketNumber,
            });
        }
    }

    // Update register height (height = number of dockets written, i.e., DocketNumber + 1)
    var oldHeight = register.Height;
    await repository.UpdateRegisterHeightAsync(registerId, (uint)(request.DocketNumber + 1));

    // Publish events and route notifications for confirmed transactions.
    // This connects the docket write path (Validator → Register) to the
    // full notification pipeline: Redis stream → RegisterEventBridge → SignalR,
    // bloom filter → Wallet Service gRPC → user notification delivery.
    if (request.Transactions is not null)
    {
        foreach (var tx in request.Transactions)
        {
            try
            {
                // Publish TransactionConfirmedEvent to Redis stream
                await eventPublisher.PublishAsync(
                    "transaction:confirmed",
                    new Sorcha.Register.Core.Events.TransactionConfirmedEvent
                    {
                        TransactionId = tx.TxId,
                        RegisterId = registerId,
                        ToWallets = tx.RecipientsWallets?.ToList() ?? [],
                        SenderWallet = tx.SenderWallet,
                        PreviousTransactionId = tx.PrevTxId,
                        MetaData = tx.MetaData,
                        ConfirmedAt = DateTime.UtcNow
                    });

                // Route action transactions to local wallet owners via bloom filter
                if (tx.MetaData?.TransactionType == TransactionType.Action)
                {
                    var recipients = tx.RecipientsWallets?.ToList() ?? [];
                    if (recipients.Count > 0)
                    {
                        var matchCount = await transactionRouter.RouteTransactionAsync(
                            registerId,
                            tx.TxId,
                            TransactionType.Action,
                            recipients,
                            tx.SenderWallet,
                            tx.MetaData,
                            request.DocketNumber,
                            isRecovery: false);

                        if (matchCount > 0)
                        {
                            logger.LogInformation(
                                "DocketHeader {DocketNumber} tx {TxId}: routed to {MatchCount} local wallet(s)",
                                request.DocketNumber, tx.TxId, matchCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Best-effort: don't fail the docket write if notification delivery fails.
                // The transaction is already stored — notifications can be recovered.
                logger.LogWarning(ex,
                    "Failed to publish event/route notification for tx {TxId} in docket {DocketNumber}",
                    tx.TxId, request.DocketNumber);
            }
        }

        // Feature 137 — project a sealed CryptoPolicyUpdate's DevMode onto the register record.
        // This runs on EVERY node that writes the docket (the owner on seal, and each replica when
        // it finalises the pulled docket), so a DevMode→Normal promotion replicates consistently
        // instead of being a local-only flag flip that desyncs nodes. The validator's one-way guard
        // guarantees a CryptoPolicyUpdate can only carry DevMode=false, so any such update promotes
        // the register to Normal (encrypted); it never re-enables DevMode.
        var hasCryptoPolicyUpdate = request.Transactions.Any(t =>
            t.MetaData?.TransactionType == TransactionType.Control &&
            t.MetaData.TrackingData?.GetValueOrDefault("transactionType") == "CryptoPolicyUpdate");
        if (hasCryptoPolicyUpdate)
        {
            try
            {
                var policyRegister = await repository.GetRegisterAsync(registerId);
                if (policyRegister is { DevMode: true })
                {
                    policyRegister.DevMode = false;
                    policyRegister.UpdatedAt = DateTime.UtcNow;
                    await repository.UpdateRegisterAsync(policyRegister);
                    logger.LogInformation(
                        "Register {RegisterId} promoted DevMode→Normal by sealed CryptoPolicyUpdate in docket {DocketNumber} — field-level encryption now required",
                        registerId, request.DocketNumber);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to project CryptoPolicyUpdate DevMode onto register {RegisterId}", registerId);
            }
        }

        // Feature 108 — if this docket contains a Control transaction, invalidate the
        // local-relationship cache and publish a register:relationship-changed event.
        var hasControlTx = request.Transactions.Any(t => t.MetaData?.TransactionType == TransactionType.Control);
        if (hasControlTx)
        {
            // Fire-and-forget: PublishIfChangedAsync has its own try/catch internally,
            // but tag the task with a ContinueWith so any escape (cancellation during
            // shutdown, unexpected scheduler error) is logged rather than silently
            // abandoned to the unobserved-task pipeline.
            _ = Task.Run(() => relationshipNotifier.PublishIfChangedAsync(registerId))
                .ContinueWith(
                    t => logger.LogWarning(t.Exception,
                        "RelationshipChangeNotifier.PublishIfChangedAsync escaped for register {RegisterId}",
                        registerId),
                    TaskContinuationOptions.OnlyOnFaulted);
        }

        // Publish docket confirmed event
        try
        {
            await eventPublisher.PublishAsync(
                "docket:confirmed",
                new Sorcha.Register.Core.Events.DocketConfirmedEvent
                {
                    RegisterId = registerId,
                    DocketId = (ulong)request.DocketNumber,
                    TransactionIds = request.TransactionIds,
                    Hash = request.DocketHash,
                    TimeStamp = DateTime.UtcNow
                });

            await eventPublisher.PublishAsync(
                "register:height-updated",
                new Sorcha.Register.Core.Events.RegisterHeightUpdatedEvent
                {
                    RegisterId = registerId,
                    OldHeight = oldHeight,
                    NewHeight = (uint)(request.DocketNumber + 1),
                    UpdatedAt = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish docket/height events for docket {DocketNumber}", request.DocketNumber);
        }
    }

    return Results.Created($"/api/registers/{registerId}/dockets/{inserted.Id}", inserted);
})
.Produces(StatusCodes.Status409Conflict)
// No .WithRequestValidation() here: this is an INTERNAL validator->register endpoint carrying an
// already hash/signature-verified docket + its ledger transactions. The request-validation seam
// recurses into WriteDocketRequest.Transactions and runs user-input DataAnnotations over ledger txs —
// which wrongly rejects a genesis docket (the genesis control tx has an empty PrevTxId, failing
// TransactionModel's [StringLength(64, MinimumLength=64)]) and 400s new-register sealing. Machine-to-
// machine ledger writes must not be gated by user-input validation.
.WithName("WriteDocket")
.WithSummary("Write a confirmed docket")
.WithDescription("Writes a consensus-confirmed docket to the register. Used by Validator Service.")
.RequireAuthorization("CanWriteDockets")
.Produces<DocketHeader>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status404NotFound)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Blueprint Publishing API
// ===========================

// <summary>
// Publish a blueprint to a register
// </summary>
app.MapPost("/api/registers/{registerId}/blueprints/publish", async (
    string registerId,
    PublishBlueprintToRegisterRequest request,
    IRegisterRepository repository,
    SystemRegisterService systemRegister,
    IHashProvider hashProvider,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    Sorcha.ServiceClients.Validator.IValidatorServiceClient validatorClient,
    ISystemWalletSigningService signingService) =>
{
    // Verify register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found" });
    }

    // Verify caller has publishing rights via governance roster
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster != null && roster.ControlRecord.Attestations.Count > 0)
    {
        var hasPublishRights = roster.ControlRecord.Attestations.Any(a =>
            a.Role.ToString() is "Owner" or "Admin" or "Designer");
        if (!hasPublishRights)
        {
            return Results.Forbid();
        }
    }

    // Per-register blueprint publish — intentionally does NOT propagate to the System
    // Register (issue #297). The SSR is a curated catalog for blueprints marked as
    // system (e.g. `join-private-register-v1`) and those are published directly via
    // SystemRegisterBootstrapper / the POST /api/system-register/blueprints endpoint.
    // Walkthrough and tenant-owned blueprints stay on their target register only.
    //
    // We still probe the SSR read-only so the response carries a meaningful `version`
    // for system-catalogued blueprints; walkthrough blueprints return version 1.
    var existingEntry = await systemRegister.GetBlueprintAsync(request.BlueprintId);
    long systemVersion = existingEntry?.Version ?? 1;

    // Submit a Control transaction to the validator for validation and docket creation.
    // All transactions must go through the validator — never write directly to the register.
    //
    // CRITICAL: Compute payload hash using the same canonical serialization the Validator uses.
    // The Validator re-serializes transaction.Payload with CanonicalJsonOptions before hashing,
    // so we must hash the same canonical form — NOT the raw request JSON string.
    var controlRecordElement = System.Text.Json.JsonDocument.Parse(request.BlueprintJson).RootElement;
    var canonicalJsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var canonicalJson = System.Text.Json.JsonSerializer.Serialize(controlRecordElement, canonicalJsonOptions);
    var blueprintBytes = System.Text.Encoding.UTF8.GetBytes(canonicalJson);
    var payloadHash = hashProvider.ComputeHash(blueprintBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
    var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

    // Deterministic TxId so re-publishing the same blueprint is idempotent
    var txIdSource = System.Text.Encoding.UTF8.GetBytes($"blueprint-publish-{registerId}-{request.BlueprintId}");
    var txIdHash = hashProvider.ComputeHash(txIdSource, Sorcha.Cryptography.Enums.HashType.SHA256);
    var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

    // Chain linking: Blueprint publish PrevTxId = latest Control TX on this register.
    // All transactions except genesis must chain from a predecessor. Blueprint publish
    // transactions are Control transactions that chain from the governance control chain
    // (the genesis TX or the most recent governance/blueprint-publish Control TX).
    string? previousControlTxId = null;
    if (roster != null)
    {
        previousControlTxId = roster.LastControlTxId;
    }

    var signResult = await signingService.SignAsync(
        registerId: registerId,
        txId: txId,
        payloadHash: payloadHashHex,
        derivationPath: SorchaDerivationPaths.RegisterControl,
        transactionType: "BlueprintPublish");

    var systemSignature = new Sorcha.ServiceClients.Validator.SignatureInfo
    {
        PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
        SignatureValue = Base64Url.EncodeToString(signResult.Signature),
        Algorithm = signResult.Algorithm
    };

    var submission = new Sorcha.ServiceClients.Validator.TransactionSubmission
    {
        TransactionId = txId,
        RegisterId = registerId,
        BlueprintId = request.BlueprintId,
        ActionId = "blueprint-publish",
        Payload = controlRecordElement,
        PayloadHash = payloadHashHex,
        PreviousTransactionId = previousControlTxId,
        Signatures = new List<Sorcha.ServiceClients.Validator.SignatureInfo> { systemSignature },
        CreatedAt = DateTimeOffset.UtcNow,
        Metadata = new Dictionary<string, string>
        {
            // Persisted TransactionType (the validator parses this via Enum.TryParse, ignoreCase=true).
            // Pre-#876 publishes were "Control" + TrackingData; we now write a dedicated
            // BlueprintPublish enum so roster, governance, and crypto-policy projections that
            // filter on TransactionType.Control naturally exclude publishes without having to
            // discriminate on free-form TrackingData strings. PR #871's reader-side defence stays
            // as belt-and-braces for legacy registers; both eras coexist forever.
            ["Type"] = "BlueprintPublish",
            // Retained for legacy log/audit consumers that already scan TrackingData. Safe to keep.
            ["transactionType"] = "BlueprintPublish",
            ["publishedBy"] = request.PublishedBy,
            ["SystemWalletAddress"] = signResult.WalletAddress,
            // Feature 138 US4 — seal the canonical content hash so recovering nodes can verify the
            // blueprint they receive against a sealed digest rather than trusting the transport.
            // payloadHashHex is already SHA-256 over the canonical blueprint JSON (see above).
            ["contentHash"] = payloadHashHex
        }
    };

    var submissionResult = await validatorClient.SubmitTransactionAsync(submission);
    if (!submissionResult.Success)
    {
        return Results.Problem(
            title: "Validator submission failed",
            detail: submissionResult.ErrorMessage ?? "The validator service rejected the blueprint publish transaction. Check validator logs.",
            statusCode: 502);
    }

    return Results.Ok(new
    {
        blueprintId = request.BlueprintId,
        registerId,
        txId,
        version = systemVersion,
        submitted = true
    });
})
.WithTags("Blueprints")
.WithRequestValidation()
.WithName("PublishBlueprintToRegister")
.WithSummary("Publish a blueprint to a register")
.WithDescription("Publishes a blueprint to a specific register after verifying governance rights.")
.RequireAuthorization("CanSubmitTransactions")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status403Forbidden)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get all published blueprints for a register (for recovery/discovery)
// </summary>
app.MapGet("/api/registers/{registerId}/blueprints/published", async (
    string registerId,
    IRegisterRepository repository) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found" });
    }

    // Query all transactions then filter in-memory to blueprint-publish transactions.
    // Post-#876 publishes are TransactionType.BlueprintPublish; pre-#876 publishes were
    // TransactionType.Control + TrackingData["transactionType"]="BlueprintPublish".
    // Both eras coexist forever, so this filter must accept either.
    // Pushed down: two index-backed type queries (BlueprintPublish + pre-#876 Control), then the
    // BlueprintId filter in memory over that small subset — avoids materialising the whole ledger.
    var byPublish = await repository.GetTransactionsByTypeAsync(
        registerId, TransactionType.BlueprintPublish, TransactionSort.TimeStampDescending);
    var byControl = await repository.GetTransactionsByTypeAsync(
        registerId, TransactionType.Control, TransactionSort.TimeStampDescending);
    var publishTransactions = byPublish.Concat(byControl)
        .Where(tx => tx.MetaData != null
            && !string.IsNullOrEmpty(tx.MetaData.BlueprintId)
            && tx.MetaData.BlueprintId != "genesis")
        .OrderByDescending(tx => tx.TimeStamp)
        .ToList();

    var blueprints = publishTransactions.Select(tx =>
    {
        var blueprintId = tx.MetaData?.BlueprintId ?? "unknown";
        var publishedBy = tx.MetaData?.TrackingData?.GetValueOrDefault("publishedBy", "system") ?? "system";
        // The blueprint JSON is in the first payload (base64-encoded on the ledger)
        var rawPayload = tx.Payloads?.FirstOrDefault()?.Data ?? "";
        string blueprintJson;
        try
        {
            // Payload data is Base64Url-encoded (via DocketSerializer → MongoDocumentMapper round-trip)
            var bytes = Base64Url.DecodeFromChars(rawPayload.AsSpan());
            blueprintJson = System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            // Already plain text
            blueprintJson = rawPayload;
        }

        // Feature 138 US4 — the canonical content hash sealed at publish time. Sourced from the
        // sealed transaction metadata (NOT recomputed from blueprintJson here), so a recovering node
        // comparing its own recomputed hash against this value detects tampering in transit.
        var contentHash = tx.MetaData?.TrackingData?.GetValueOrDefault("contentHash", "") ?? "";

        return new
        {
            blueprintId,
            transactionId = tx.TxId,
            publishedBy,
            publishedAt = tx.TimeStamp,
            blueprintJson,
            contentHash
        };
    }).ToList();

    return Results.Ok(new
    {
        registerId,
        blueprints,
        registerHeight = register.Height,
        queriedAt = DateTimeOffset.UtcNow
    });
})
.WithName("GetPublishedBlueprints")
.WithSummary("Get published blueprints for a register")
.WithDescription("Returns all blueprint-publish control transactions for a register. Used by Blueprint Service during startup recovery to rebuild the published blueprint index.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.AllowAnonymous(); // Internal recovery endpoint — no auth required (returns only metadata)

// ===========================
// Governance API
// ===========================

var governanceGroup = app.MapGroup("/api/registers/{registerId}/governance")
    .WithTags("Governance")
    .RequireAuthorization("CanReadTransactions");

// <summary>
// Get the current admin roster for a register
// </summary>
governanceGroup.MapGet("/roster", async (
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    string registerId) =>
{
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster == null)
    {
        return Results.NotFound(new { error = $"No governance roster found for register '{registerId}'" });
    }

    return Results.Ok(new
    {
        roster.RegisterId,
        Members = roster.ControlRecord.Attestations.Select(a => new
        {
            a.Subject,
            Role = a.Role.ToString(),
            a.Algorithm,
            a.GrantedAt
        }),
        MemberCount = roster.ControlRecord.Attestations.Count,
        roster.ControlTransactionCount,
        roster.LastControlTxId
    });
})
.WithName("GetGovernanceRoster")
.WithSummary("Get current admin roster")
.WithDescription("Reconstructs the current admin roster by replaying all Control transactions for the register.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get governance history (Control transactions)
// </summary>
governanceGroup.MapGet("/history", async (
    IRegisterRepository repository,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    // Pushed down: Control transactions, docket descending (index-backed) — then page in memory.
    var controlTxs = await repository.GetTransactionsByTypeAsync(
        registerId, TransactionType.Control, TransactionSort.DocketNumberDescending);

    var pagedTxs = controlTxs
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();

    return Results.Ok(new
    {
        Page = page,
        PageSize = pageSize,
        Total = controlTxs.Count,
        Transactions = pagedTxs
    });
})
.WithName("GetGovernanceHistory")
.WithSummary("Get governance history")
.WithDescription("Retrieves paginated Control transactions that make up the governance history for a register.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Submit a governance proposal (add/remove member, transfer ownership)
// </summary>
governanceGroup.MapPost("/propose", async (
    string registerId,
    GovernanceProposalRequest request,
    IRegisterRepository repository,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    IHashProvider hashProvider,
    Sorcha.ServiceClients.Validator.IValidatorServiceClient validatorClient,
    IGovernanceSigningService signingService) =>
{
    // 1. Verify register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found" });
    }

    // 2. Reconstruct current roster
    var roster = await rosterService.GetCurrentRosterAsync(registerId);
    if (roster == null)
    {
        return Results.Problem(
            title: "No governance roster",
            detail: $"Register '{registerId}' has no governance roster. A genesis Control transaction is required first.",
            statusCode: 422);
    }

    // 3. Build governance operation from request
    var operation = new GovernanceOperation
    {
        OperationType = request.OperationType,
        ProposerDid = request.ProposerDid,
        TargetDid = request.TargetDid,
        TargetRole = request.TargetRole ?? RegisterRole.Admin,
        ApprovalSignatures = request.ApprovalSignatures ?? [],
        ProposedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        Status = ProposalStatus.Pending,
        Justification = request.Justification,
        // Feature 189 (FR-011a): freeze the roster and the rule this proposal is judged against, so
        // neither the eligible approvers nor the number required can shift while it is open. The
        // validator refuses the proposal outright if the roster has moved on (FR-011b) rather than
        // re-counting against a changed pool — otherwise removing a dissenter would turn a blocked
        // change into an enacted one.
        RosterSnapshotId = roster.LastControlTxId,
        QuorumFormulaAtRaise = roster.ControlRecord.RegisterPolicy?.Governance?.QuorumFormula
                               ?? QuorumFormula.StrictMajority
    };

    // 4. Validate proposal against current roster
    var validationResult = rosterService.ValidateProposal(roster, operation);
    if (!validationResult.IsValid)
    {
        return Results.BadRequest(new
        {
            error = "Governance proposal validation failed",
            errors = validationResult.Errors
        });
    }

    // 5. Validate quorum (owner override for Add/Remove, quorum required for Transfer)
    var quorumResult = await rosterService.ValidateQuorumAsync(
        registerId, operation, operation.ApprovalSignatures);
    if (!quorumResult.IsQuorumMet)
    {
        return Results.BadRequest(new
        {
            error = "Quorum not met",
            votesRequired = quorumResult.VotesRequired,
            votesReceived = quorumResult.VotesReceived,
            votingPool = quorumResult.VotingPool,
            isOwnerOverride = quorumResult.IsOwnerOverride
        });
    }

    // 6. Apply operation to produce updated roster
    RegisterAttestation? newAttestation = null;
    if (operation.OperationType == GovernanceOperationType.Add)
    {
        newAttestation = new RegisterAttestation
        {
            Role = operation.TargetRole,
            Subject = operation.TargetDid,
            PublicKey = string.Empty,
            Signature = string.Empty,
            Algorithm = Sorcha.Register.Models.SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };
    }

    // Validator roster operations (AddValidator, RemoveValidator, RotateValidatorKey)
    if (operation.OperationType is GovernanceOperationType.AddValidator
        or GovernanceOperationType.RemoveValidator
        or GovernanceOperationType.RotateValidatorKey)
    {
        var validatorRoster = roster.ControlRecord.Validators ?? new ValidatorRoster { Version = 0 };

        switch (operation.OperationType)
        {
            case GovernanceOperationType.AddValidator:
                if (operation.ValidatorEntry == null)
                    return Results.BadRequest(new { error = "ValidatorEntry is required for AddValidator operation" });
                operation.ValidatorEntry.AuthorizedAt = DateTimeOffset.UtcNow;
                operation.ValidatorEntry.Status = ValidatorKeyStatus.Active;
                validatorRoster.Validators.Add(operation.ValidatorEntry);
                break;

            case GovernanceOperationType.RemoveValidator:
                var toRevoke = validatorRoster.Validators.FirstOrDefault(v => v.ValidatorId == operation.TargetDid);
                if (toRevoke == null)
                    return Results.BadRequest(new { error = $"Validator '{operation.TargetDid}' not found in roster" });
                if (validatorRoster.ActiveValidators.Count() <= 1)
                    return Results.BadRequest(new { error = "Cannot remove the last active validator" });
                toRevoke.Status = ValidatorKeyStatus.Revoked;
                toRevoke.RevokedAt = DateTimeOffset.UtcNow;
                break;

            case GovernanceOperationType.RotateValidatorKey:
                if (operation.ValidatorEntry == null)
                    return Results.BadRequest(new { error = "ValidatorEntry is required for RotateValidatorKey operation" });
                var toRotate = validatorRoster.Validators.FirstOrDefault(v => v.ValidatorId == operation.TargetDid && v.Status == ValidatorKeyStatus.Active);
                if (toRotate == null)
                    return Results.BadRequest(new { error = $"Active validator '{operation.TargetDid}' not found in roster" });
                toRotate.Status = ValidatorKeyStatus.Rotated;
                toRotate.RevokedAt = DateTimeOffset.UtcNow;
                operation.ValidatorEntry.AuthorizedAt = DateTimeOffset.UtcNow;
                operation.ValidatorEntry.Status = ValidatorKeyStatus.Active;
                validatorRoster.Validators.Add(operation.ValidatorEntry);
                break;
        }

        validatorRoster.Version++;

        var rosterErrors = validatorRoster.Validate();
        if (rosterErrors.Count > 0)
            return Results.BadRequest(new { error = "Validator roster validation failed", errors = rosterErrors });

        roster.ControlRecord.Validators = validatorRoster;
    }

    operation.Status = ProposalStatus.Approved;
    var updatedRoster = rosterService.ApplyOperation(
        roster.ControlRecord, operation, newAttestation);

    // 7. Build ControlTransactionPayload
    var payload = new ControlTransactionPayload
    {
        Version = 1,
        Roster = updatedRoster,
        Operation = operation
    };

    // 8. Canonical JSON serialization for deterministic hashing
    var canonicalJsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, canonicalJsonOptions);
    var payloadElement = System.Text.Json.JsonDocument.Parse(payloadJson).RootElement;
    var canonicalJson = System.Text.Json.JsonSerializer.Serialize(payloadElement, canonicalJsonOptions);
    var payloadBytes = System.Text.Encoding.UTF8.GetBytes(canonicalJson);
    var payloadHash = hashProvider.ComputeHash(payloadBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
    var payloadHashHex = Convert.ToHexString(payloadHash).ToLowerInvariant();

    // 9. Deterministic TxId for idempotency
    var opType = operation.OperationType.ToString().ToLowerInvariant();
    var txIdSource = System.Text.Encoding.UTF8.GetBytes(
        $"governance-{opType}-{registerId}-{operation.ProposerDid}-{operation.TargetDid}-{operation.ProposedAt:O}");
    var txIdHash = hashProvider.ComputeHash(txIdSource, Sorcha.Cryptography.Enums.HashType.SHA256);
    var txId = Convert.ToHexString(txIdHash).ToLowerInvariant();

    // 10. Chain linking from latest Control TX
    string? previousControlTxId = roster.LastControlTxId;

    // 11. Sign as the proposing ORGANISATION at slot 100 (R-020).
    //
    // This previously signed with SorchaDerivationPaths.RegisterControl — slot 101, the NODE's system
    // wallet. A register's governance roster is built from its genesis attestations, which record the
    // ORGANISATION's slot-100 key, so a node-signed proposal is refused by RightsEnforcementService as
    // "submitter not found in roster" on any register whose genesis has sealed. US1 moved
    // /disable-dev-mode and /governance/crypto-policy across and left this path behind, which meant
    // every roster change — Add, Remove, Transfer and all validator operations — could not complete.
    var signResult = await signingService.SignAsync(
        registerId: registerId,
        txId: txId,
        payloadHash: payloadHashHex,
        preferredSubject: null);   // Owner signs; consortium selection is US2

    var organisationSignature = new Sorcha.ServiceClients.Validator.SignatureInfo
    {
        PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
        SignatureValue = Base64Url.EncodeToString(signResult.Signature),
        Algorithm = signResult.Algorithm
    };

    // 12. Submit as Control TX via validator
    var submission = new Sorcha.ServiceClients.Validator.TransactionSubmission
    {
        TransactionId = txId,
        RegisterId = registerId,
        // Feature 189 (R-005): an EMPTY BlueprintId is rejected by TransactionValidator with
        // TX_003 "Blueprint ID is required" before any governance handling runs — so this endpoint
        // could never have worked. "genesis" would be worse: TransactionTypeClassifier
        // .IsGenesisTransaction matches that exact value and would judge a routine governance
        // operation against the short GenesisMaxAge freshness window. The governance control
        // blueprint is the correct value, and it also opts the transaction into the roster
        // enforcement it should always have had.
        BlueprintId = Sorcha.Register.Service.Services.CryptoPolicyService.GovernanceBlueprintId,
        ActionId = $"governance-{opType}",
        Payload = payloadElement,
        PayloadHash = payloadHashHex,
        PreviousTransactionId = previousControlTxId,
        Signatures = new List<Sorcha.ServiceClients.Validator.SignatureInfo> { organisationSignature },
        CreatedAt = DateTimeOffset.UtcNow,
        Metadata = new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "GovernanceOperation",
            ["operationType"] = opType,
            ["proposerDid"] = operation.ProposerDid,
            ["targetDid"] = operation.TargetDid,
            ["SystemWalletAddress"] = signResult.WalletAddress
        }
    };

    var submissionResult = await validatorClient.SubmitTransactionAsync(submission);
    if (!submissionResult.Success)
    {
        return Results.Problem(
            title: "Validator submission failed",
            detail: submissionResult.ErrorMessage ?? "The validator rejected the governance transaction.",
            statusCode: 502);
    }

    return Results.Ok(new
    {
        txId,
        registerId,
        operationType = opType,
        proposerDid = operation.ProposerDid,
        targetDid = operation.TargetDid,
        targetRole = operation.TargetRole.ToString(),
        quorum = new
        {
            quorumResult.IsQuorumMet,
            quorumResult.VotesRequired,
            quorumResult.VotesReceived,
            quorumResult.IsOwnerOverride
        },
        submitted = true
    });
})
.WithRequestValidation()
.WithName("ProposeGovernanceOperation")
.WithSummary("Submit a governance proposal")
.WithDescription("Submits a governance operation (Add, Remove, Transfer) as a Control transaction. Owner can Add/Remove without quorum. Transfer requires quorum.")
.RequireAuthorization("CanSubmitTransactions")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// List governance proposals from Control TX history
// </summary>
// <summary>
// Feature 189 T076 — what an approver must sign.
// </summary>
// The proposal IS a ledger transaction, so its TxId is the proposal id.
//
// This deliberately returns NO DIGEST (FR-028). A server-supplied digest could fail to match the
// operation the client displayed, reinstating at the transport layer exactly the substitution that
// statement v2 closes inside the digest. The client derives the digest from the operation it
// rendered, so the two cannot disagree — and it must render it, because signing an opaque value is
// not approval (FR-027).
governanceGroup.MapGet("/proposals/{proposalId}/signing-request", async (
    IReadOnlyRegisterRepository repository,
    string registerId,
    string proposalId,
    string approverDid,
    CancellationToken ct) =>
{
    var tx = await repository.GetTransactionAsync(registerId, proposalId, ct);
    if (tx is null)
    {
        return Results.NotFound(new { error = $"No proposal '{proposalId}' on register '{registerId}'" });
    }

    var trackingType = tx.MetaData?.TrackingData?.GetValueOrDefault("transactionType");
    if (!string.Equals(trackingType, "GovernanceOperation", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = $"Transaction '{proposalId}' is not a governance proposal" });
    }

    // Same decode path the genesis-attestation reader uses: Data is base64 or base64url.
    GovernanceOperation? operation = null;
    try
    {
        var payloadData = tx.Payloads.Length > 0 ? tx.Payloads[0].Data : null;
        if (!string.IsNullOrWhiteSpace(payloadData))
        {
            var payloadBytes = payloadData.Contains('+') || payloadData.Contains('/') || payloadData.Contains('=')
                ? Convert.FromBase64String(payloadData)
                : System.Buffers.Text.Base64Url.DecodeFromChars(payloadData);

            operation = System.Text.Json.JsonSerializer
                .Deserialize<ControlTransactionPayload>(
                    payloadBytes,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?.Operation;
        }
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
    {
        // A payload that will not decode is reported, never approximated (see below).
        operation = null;
    }

    if (operation is null)
    {
        // Refused rather than approximated: an approver must see exactly what their signature binds,
        // and a partially-reconstructed operation is the substitution risk in another form.
        return Results.Problem(
            detail: $"The proposal payload for '{proposalId}' could not be read as a governance operation.",
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    return Results.Ok(new GovernanceSigningRequest
    {
        RequestId = proposalId,
        RegisterId = registerId,
        Operation = operation,
        StatementVersion = GovernanceApprovalStatement.StatementVersion,
        ApproverDid = approverDid,
        ExpiresAt = operation.ExpiresAt
    });
})
.WithName("GetGovernanceSigningRequest")
.WithSummary("Get what an approver must sign for a governance proposal")
.WithDescription(
    "Returns the full governance operation an approving organisation is being asked to authorise. "
    + "Carries no digest by design — the client derives it from the operation it rendered, so a "
    + "server-supplied digest cannot disagree with what the approver actually saw.");

governanceGroup.MapGet("/proposals", async (
    IRegisterRepository repository,
    Sorcha.Register.Core.Services.IGovernanceRosterService rosterService,
    string registerId,
    int page = 1,
    int pageSize = 20) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
    {
        return Results.NotFound(new { error = "Register not found" });
    }

    // Pushed down: Control transactions, docket descending (index-backed) — then filter/page in memory.
    var controlTxs = await repository.GetTransactionsByTypeAsync(
        registerId, TransactionType.Control, TransactionSort.DocketNumberDescending);

    // Filter to Control TXs that have governance operation metadata
    var governanceProposals = controlTxs
        .Where(t => t.MetaData?.TrackingData != null
            && t.MetaData.TrackingData.ContainsKey("transactionType")
            && t.MetaData.TrackingData["transactionType"] == "GovernanceOperation")
        .ToList();

    var total = governanceProposals.Count;
    var pagedTxs = governanceProposals
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(t => new
        {
            t.TxId,
            t.DocketNumber,
            t.TimeStamp,
            OperationType = t.MetaData?.TrackingData?.GetValueOrDefault("operationType"),
            ProposerDid = t.MetaData?.TrackingData?.GetValueOrDefault("proposerDid"),
            TargetDid = t.MetaData?.TrackingData?.GetValueOrDefault("targetDid")
        })
        .ToList();

    return Results.Ok(new
    {
        Page = page,
        PageSize = pageSize,
        Total = total,
        Proposals = pagedTxs
    });
})
.WithName("GetGovernanceProposals")
.WithSummary("List governance proposals")
.WithDescription("Returns paginated governance operations from Control transaction history.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Crypto Policy API
// ===========================

var cryptoPolicyGroup = app.MapGroup("/api/registers/{registerId}/crypto-policy")
    .WithTags("CryptoPolicy")
    .RequireAuthorization("CanReadTransactions");

// <summary>
// Get the active crypto policy for a register
// </summary>
cryptoPolicyGroup.MapGet("/", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    string registerId,
    CancellationToken ct) =>
{
    var policy = await cryptoPolicyService.GetActivePolicyAsync(registerId, ct);
    return Results.Ok(policy);
})
.WithName("GetActiveCryptoPolicy")
.WithSummary("Get active crypto policy")
.WithDescription("Returns the active cryptographic policy for this register. If no explicit policy has been set, returns the default permissive policy accepting all algorithms.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get crypto policy version history for a register
// </summary>
cryptoPolicyGroup.MapGet("/history", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    string registerId,
    CancellationToken ct) =>
{
    var history = await cryptoPolicyService.GetPolicyHistoryAsync(registerId, ct);
    return Results.Ok(new { Versions = history, Total = history.Count });
})
.WithName("GetCryptoPolicyHistory")
.WithSummary("Get crypto policy version history")
.WithDescription("Returns all crypto policy versions for this register, ordered by version number. Includes the genesis policy and all subsequent updates.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Submit a crypto policy update as a control transaction
// </summary>
governanceGroup.MapPost("/crypto-policy", async (
    Sorcha.Register.Service.Services.CryptoPolicyService cryptoPolicyService,
    string registerId,
    Sorcha.Register.Models.CryptoPolicy policyUpdate,
    CancellationToken ct) =>
{
    // Validate the policy
    if (!policyUpdate.IsValid())
    {
        return Results.BadRequest(new { Error = "Invalid crypto policy: RequiredSignatureAlgorithms must be a subset of AcceptedSignatureAlgorithms, and all algorithm arrays must be non-empty." });
    }

    try
    {
        var submitted = await cryptoPolicyService.SubmitPolicyUpdateAsync(
            registerId, policyUpdate, updatedBy: "governance", ct);

        return Results.Ok(new
        {
            TxId = submitted.TransactionId,
            PolicyVersion = submitted.PolicyVersion,
            Status = "submitted"
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(
            title: "Crypto policy update rejected",
            detail: ex.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }
})
.WithName("UpdateCryptoPolicy")
.WithSummary("Update register crypto policy")
.WithDescription("Submits a crypto policy update as a control transaction via the Validator. The new policy takes effect on every node once the control transaction seals into a docket. A policy re-enabling DevMode is refused (422) — the DevMode→Normal transition is one-way.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status422UnprocessableEntity)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// NOTE: `PUT /api/registers/{registerId}/devmode` has been REMOVED (deliberately, no replacement).
//
// It wrote `Register.DevMode` straight to the repository, which made it a bidirectional local flag
// flip: it could re-enable DevMode on a Normal register, reverting new submissions to plaintext.
// That defeated the consensus-level one-way guarantee in
// ControlDocketProcessor.ValidateCryptoPolicyUpdate, because emitting no control transaction meant
// that guard never ran. It also never replicated, so it desynced the owner from its replicas.
//
// A register is born into its DevMode posture at genesis (RegisterCreationOrchestrator, from the
// creation request) and may only ever be promoted DevMode→Normal, via
// `POST /api/registers/{registerId}/disable-dev-mode`, which submits a crypto-policy control
// transaction through the Validator so the change seals into a docket and replicates.
// Do NOT reintroduce a direct-write toggle.

// ===========================
// Participant Query API
// ===========================

var participantsGroup = app.MapGroup("/api/registers/{registerId}/participants")
    .WithTags("Participants")
    .RequireAuthorization("CanReadTransactions");

// <summary>
// List published participants on a register
// </summary>
participantsGroup.MapGet("/", (
    ParticipantIndexService index,
    string registerId,
    int skip = 0,
    int top = 20,
    string? status = "active") =>
{
    var page = index.List(registerId, skip, top, status);
    return Results.Ok(page);
})
.WithName("ListParticipants")
.WithSummary("List published participants")
.WithDescription("Returns a paginated list of published participant records on this register. Defaults to active participants only. Use status=all to include deprecated/revoked.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Look up a participant by wallet address
// </summary>
participantsGroup.MapGet("/by-address/{walletAddress}", (
    ParticipantIndexService index,
    string registerId,
    string walletAddress) =>
{
    var record = index.GetByAddress(registerId, walletAddress);
    return record is not null ? Results.Ok(record) : Results.NotFound(new { error = "No participant found for this wallet address" });
})
.WithName("GetParticipantByAddress")
.WithSummary("Look up participant by wallet address")
.WithDescription("Returns the published participant record that owns the specified wallet address on this register.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get a participant by ID
// </summary>
participantsGroup.MapGet("/{participantId}", (
    ParticipantIndexService index,
    string registerId,
    string participantId) =>
{
    var record = index.GetById(registerId, participantId);
    return record is not null ? Results.Ok(record) : Results.NotFound(new { error = "Participant not found" });
})
.WithName("GetParticipantById")
.WithSummary("Get participant by ID")
.WithDescription("Returns the latest published version of a participant record by participant ID.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Resolve a participant by blueprint role ID and organisation name
// </summary>
participantsGroup.MapGet("/resolve", (
    ParticipantIndexService index,
    string registerId,
    string participantId,
    string? orgName = null) =>
{
    var record = index.Resolve(registerId, participantId, orgName);
    if (record is null)
        return Results.NotFound(new { error = "No published participant record found" });

    if (string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status410Gone,
            title: "Participant Revoked",
            detail: $"Participant '{record.ParticipantId}' has been revoked");
    }

    return Results.Ok(new
    {
        participantId = record.ParticipantId,
        participantName = record.ParticipantName,
        organisationName = record.OrganizationName,
        status = record.Status,
        addresses = record.Addresses.Select(a => new
        {
            walletAddress = a.WalletAddress,
            publicKey = a.PublicKey,
            algorithm = a.Algorithm,
            primary = a.Primary
        })
    });
})
.WithName("ResolveParticipant")
.WithSummary("Resolve participant by role ID and organisation")
.WithDescription("Resolves a participant by their blueprint role ID and optional organisation name. Returns the published participant record with wallet addresses.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status410Gone)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Resolve a participant's public key by wallet address
// </summary>
participantsGroup.MapGet("/by-address/{walletAddress}/public-key", (
    ParticipantIndexService index,
    string registerId,
    string walletAddress,
    string? algorithm = null) =>
{
    var record = index.GetByAddress(registerId, walletAddress);
    if (record == null)
        return Results.NotFound(new { error = "No participant found for this wallet address" });

    // Revoked participants return 410 Gone
    if (string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status410Gone,
            title: "Participant Revoked",
            detail: $"Participant '{record.ParticipantId}' has been revoked");
    }

    // Find the matching address entry
    var addressInfo = !string.IsNullOrEmpty(algorithm)
        ? record.Addresses.FirstOrDefault(a => string.Equals(a.Algorithm, algorithm, StringComparison.OrdinalIgnoreCase))
        : record.Addresses.FirstOrDefault(a => a.Primary) ?? record.Addresses.FirstOrDefault();

    if (addressInfo == null)
        return Results.NotFound(new { error = $"No address found with algorithm '{algorithm}'" });

    return Results.Ok(new Sorcha.ServiceClients.Register.Models.PublicKeyResolution
    {
        ParticipantId = record.ParticipantId,
        ParticipantName = record.ParticipantName,
        WalletAddress = addressInfo.WalletAddress,
        PublicKey = addressInfo.PublicKey,
        Algorithm = addressInfo.Algorithm,
        Status = record.Status
    });
})
.WithName("ResolvePublicKey")
.WithSummary("Resolve public key by wallet address")
.WithDescription("Returns the public key for field-level encryption. Returns 410 Gone if participant is revoked.")
.Produces<Sorcha.ServiceClients.Register.Models.PublicKeyResolution>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status410Gone)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Batch resolve public keys for multiple wallet addresses
// </summary>
participantsGroup.MapPost("/resolve-public-keys", (
    ParticipantIndexService index,
    string registerId,
    Sorcha.ServiceClients.Register.Models.BatchPublicKeyRequest request) =>
{
    // Validate request
    if (request.WalletAddresses == null || request.WalletAddresses.Length == 0)
        return Results.BadRequest(new { error = "walletAddresses must contain at least one address" });

    if (request.WalletAddresses.Length > 200)
        return Results.BadRequest(new { error = "Maximum 200 addresses per request" });

    var resolved = new Dictionary<string, Sorcha.ServiceClients.Register.Models.PublicKeyResolution>();
    var notFound = new List<string>();
    var revoked = new List<string>();

    foreach (var walletAddress in request.WalletAddresses.Distinct())
    {
        var record = index.GetByAddress(registerId, walletAddress);
        if (record == null)
        {
            notFound.Add(walletAddress);
            continue;
        }

        if (string.Equals(record.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            revoked.Add(walletAddress);
            continue;
        }

        // Find matching address entry (same logic as single resolve)
        var addressInfo = !string.IsNullOrEmpty(request.Algorithm)
            ? record.Addresses.FirstOrDefault(a => string.Equals(a.Algorithm, request.Algorithm, StringComparison.OrdinalIgnoreCase))
            : record.Addresses.FirstOrDefault(a => a.Primary) ?? record.Addresses.FirstOrDefault();

        if (addressInfo == null)
        {
            notFound.Add(walletAddress);
            continue;
        }

        resolved[walletAddress] = new Sorcha.ServiceClients.Register.Models.PublicKeyResolution
        {
            ParticipantId = record.ParticipantId,
            ParticipantName = record.ParticipantName,
            WalletAddress = addressInfo.WalletAddress,
            PublicKey = addressInfo.PublicKey,
            Algorithm = addressInfo.Algorithm,
            Status = record.Status
        };
    }

    return Results.Ok(new Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse
    {
        Resolved = resolved,
        NotFound = notFound.ToArray(),
        Revoked = revoked.ToArray()
    });
})
.WithName("ResolvePublicKeysBatch")
.WithSummary("Batch resolve public keys")
.WithDescription("Resolves public keys for multiple wallet addresses. Returns resolved, not-found, and revoked addresses separately. Max 200 addresses per request.")
.Produces<Sorcha.ServiceClients.Register.Models.BatchPublicKeyResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Zero-Knowledge Proof API
// ===========================

var proofsGroup = app.MapGroup("/api/registers/{registerId}/proofs")
    .WithTags("ZK Proofs")
    .RequireAuthorization("CanReadTransactions");

// <summary>
// Generate a ZK inclusion proof for a transaction in a docket
// </summary>
proofsGroup.MapPost("/inclusion", async (
    IRegisterRepository repository,
    IHashProvider hashProvider,
    string registerId,
    InclusionProofRequest request) =>
{
    // Validate register exists
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Validate TxId format (64-char hex SHA-256)
    if (string.IsNullOrWhiteSpace(request.TxId) || request.TxId.Length != 64)
        return Results.BadRequest(new { error = "TxId must be a 64-character hex string (SHA-256)" });

    // Validate docket exists
    if (string.IsNullOrWhiteSpace(request.DocketId))
        return Results.BadRequest(new { error = "DocketId is required" });

    var dockets = await repository.GetDocketsAsync(registerId);
    var docket = dockets.FirstOrDefault(d => d.Id.ToString() == request.DocketId);
    if (docket == null)
        return Results.NotFound(new { error = $"DocketHeader {request.DocketId} not found" });

    // Verify the transaction is in the docket
    var txIds = docket.TransactionIds?.ToList() ?? [];
    if (!txIds.Contains(request.TxId, StringComparer.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Transaction not found in specified docket" });

    // Build Merkle tree and generate proof path
    var merkleTree = new Sorcha.Cryptography.Utilities.MerkleTree(hashProvider);
    var merkleRoot = merkleTree.ComputeMerkleRoot(txIds.AsReadOnly());
    var proofPath = BuildMerkleProofPath(txIds, request.TxId, hashProvider);

    // Generate ZK inclusion proof
    var txHash = Convert.FromHexString(request.TxId);
    var rootBytes = Convert.FromHexString(merkleRoot);
    var proofPathBytes = proofPath.Select(p => Convert.FromHexString(p)).ToArray();

    var zkProvider = new Sorcha.Cryptography.Core.ZKInclusionProofProvider();
    var proof = zkProvider.GenerateInclusionProof(txHash, rootBytes, proofPathBytes, request.DocketId);

    return Results.Ok(new
    {
        RegisterId = registerId,
        DocketId = request.DocketId,
        TxId = request.TxId,
        MerkleRoot = merkleRoot,
        Commitment = Convert.ToBase64String(proof.Commitment),
        ProofData = Convert.ToBase64String(proof.ProofData),
        MerkleProofPath = proofPathBytes.Select(Convert.ToBase64String).ToArray(),
        VerificationKey = Convert.ToBase64String(proof.VerificationKey)
    });
})
.WithRequestValidation()
.WithName("GenerateInclusionProof")
.WithSummary("Generate ZK inclusion proof")
.WithDescription("Generates a zero-knowledge proof that a transaction is included in a docket's Merkle tree without revealing the transaction content.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Verify a ZK inclusion proof
// </summary>
proofsGroup.MapPost("/verify-inclusion", (
    VerifyInclusionProofRequest request) =>
{
    try
    {
        var proof = new Sorcha.Cryptography.Models.ZKInclusionProof
        {
            DocketId = request.DocketId,
            MerkleRoot = Convert.FromBase64String(request.MerkleRoot),
            Commitment = Convert.FromBase64String(request.Commitment),
            ProofData = Convert.FromBase64String(request.ProofData),
            MerkleProofPath = request.MerkleProofPath.Select(Convert.FromBase64String).ToArray(),
            VerificationKey = Convert.FromBase64String(request.VerificationKey)
        };

        var zkProvider = new Sorcha.Cryptography.Core.ZKInclusionProofProvider();
        var result = zkProvider.VerifyInclusionProof(proof);

        return Results.Ok(new
        {
            IsValid = result.IsValid,
            Message = result.Message,
            DocketId = request.DocketId
        });
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "Invalid base64 encoding in proof fields" });
    }
})
.WithRequestValidation()
.WithName("VerifyInclusionProof")
.WithSummary("Verify ZK inclusion proof")
.WithDescription("Verifies a zero-knowledge proof of transaction inclusion without access to the original transaction data.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// ===========================
// Transaction Receipt API
// ===========================

var receiptsGroup = app.MapGroup("/api/registers/{registerId}")
    .WithTags("Receipts");

// <summary>
// Store a batch of transaction receipts (internal, from Validator Service)
// </summary>
receiptsGroup.MapPost("/receipts/batch", async (
    IRegisterRepository repository,
    IEventPublisher eventPublisher,
    string registerId,
    BatchReceiptRequest request) =>
{
    if (request.Receipts == null || request.Receipts.Length == 0)
        return Results.BadRequest(new { error = "Receipts array is required and must not be empty" });

    // Ensure all receipts belong to this register
    var receipts = request.Receipts.ToList();
    var mismatchedReceipt = receipts
        .FirstOrDefault(r => !string.Equals(r.RegisterId, registerId, StringComparison.OrdinalIgnoreCase));
    if (mismatchedReceipt is not null)
        return Results.BadRequest(new { error = $"Receipt {mismatchedReceipt.ReceiptId} has mismatched RegisterId" });

    await repository.InsertReceiptsAsync(receipts);

    // Publish receipt:generated event for each receipt for SignalR notification
    foreach (var receipt in receipts)
    {
        await eventPublisher.PublishAsync(
            "receipt:generated",
            new ReceiptGeneratedEvent
            {
                RegisterId = registerId,
                DocketNumber = request.DocketNumber,
                Count = receipts.Count,
                TransactionId = receipt.TransactionId,
                ReceiptId = receipt.ReceiptId,
                SealedAt = receipt.SealedAt,
                GeneratedAt = DateTime.UtcNow
            });
    }

    return Results.Created(
        $"/api/registers/{registerId}/dockets/{request.DocketNumber}/receipts",
        new { stored = receipts.Count, docketNumber = request.DocketNumber });
})
.WithRequestValidation()
.WithName("StoreBatchReceipts")
.WithSummary("Store batch of transaction receipts (internal)")
.WithDescription("Stores a batch of transaction receipts generated by the Validator Service after docket sealing. Internal endpoint for service-to-service communication.")
.RequireAuthorization("CanWriteDockets")
.Produces<object>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.ProducesValidationProblem()
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get receipt for a specific transaction
// </summary>
receiptsGroup.MapGet("/transactions/{txId}/receipt", async (
    IRegisterRepository repository,
    string registerId,
    string txId) =>
{
    var receipt = await repository.GetReceiptByTxIdAsync(registerId, txId);
    return receipt is not null ? Results.Ok(receipt) : Results.NotFound();
})
.WithName("GetTransactionReceipt")
.WithSummary("Get transaction receipt")
.WithDescription("Retrieves the cryptographic receipt for a specific transaction, including the Merkle inclusion proof and validator signatures.")
.RequireAuthorization("CanReadTransactions")
.Produces<TransactionReceipt>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Get all receipts for a docket
// </summary>
receiptsGroup.MapGet("/dockets/{docketNumber:long}/receipts", async (
    IRegisterRepository repository,
    string registerId,
    long docketNumber,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? page,
    [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize) =>
{
    var effectivePage = Math.Max(1, page ?? 1);
    var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 100);

    var (receipts, total) = await repository.GetReceiptsByDocketAsync(
        registerId, docketNumber, effectivePage, effectivePageSize);

    return Results.Ok(new
    {
        Page = effectivePage,
        PageSize = effectivePageSize,
        Total = total,
        Receipts = receipts
    });
})
.WithName("GetDocketReceipts")
.WithSummary("Get docket receipts")
.WithDescription("Retrieves paginated transaction receipts for a specific docket number.")
.RequireAuthorization("CanReadTransactions")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Verify a transaction receipt
// </summary>
receiptsGroup.MapPost("/receipts/verify", (
    IHashProvider hashProvider,
    VerifyReceiptRequest request) =>
{
    if (request.Receipt == null)
        return Results.BadRequest(new { error = "Receipt is required" });

    if (string.IsNullOrWhiteSpace(request.ValidatorPublicKey))
        return Results.BadRequest(new { error = "ValidatorPublicKey is required" });

    try
    {
        var proofValidator = new InclusionProofValidator(hashProvider);
        var receiptValidator = new ReceiptValidator(proofValidator);
        var result = receiptValidator.Verify(request.Receipt, request.ValidatorPublicKey);

        return Results.Ok(new
        {
            isValid = result.IsValid,
            checks = new
            {
                signatureValid = result.SignatureValid,
                inclusionProofValid = result.InclusionProofValid,
                merkleRootConsistent = result.MerkleRootConsistent
            },
            errors = result.Errors
        });
    }
    catch (FormatException ex)
    {
        return Results.BadRequest(new { error = $"Verification failed: {ex.Message}" });
    }
    catch (System.Security.Cryptography.CryptographicException ex)
    {
        return Results.BadRequest(new { error = $"Verification failed: {ex.Message}" });
    }
})
.WithRequestValidation()
.WithName("VerifyReceipt")
.WithSummary("Verify a transaction receipt (public)")
.WithDescription("Verifies a transaction receipt's validator signature, Merkle inclusion proof, and root consistency. No authentication required.")
.AllowAnonymous()
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// ===========================
// Admin / Diagnostic Endpoints
// ===========================

var adminGroup = app.MapGroup("/api/admin/registers/{registerId}")
    .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
    .WithTags("Admin");

// <summary>
// Detect orphan transactions (not referenced by any docket)
// </summary>
adminGroup.MapGet("/orphan-transactions", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Get all dockets and collect their transaction IDs
    var dockets = await repository.GetDocketsAsync(registerId);
    var dockedTxIds = new HashSet<string>(
        dockets.SelectMany(d => d.TransactionIds ?? []),
        StringComparer.OrdinalIgnoreCase);

    // Get all transactions
    var allTxQueryable = await repository.GetTransactionsAsync(registerId);
    var allTransactions = allTxQueryable.ToList();

    // Orphans = transactions not referenced by any docket
    var orphans = allTransactions
        .Where(tx => !dockedTxIds.Contains(tx.TxId))
        .Select(tx => new
        {
            tx.TxId,
            tx.RegisterId,
            tx.DocketNumber,
            tx.SenderWallet,
            tx.TimeStamp,
            tx.PrevTxId,
            HasSignature = !string.IsNullOrEmpty(tx.Signature),
            MetadataType = tx.MetaData?.TransactionType.ToString(),
            PayloadCount = tx.PayloadCount
        })
        .ToList();

    return Results.Ok(new
    {
        RegisterId = registerId,
        TotalTransactions = allTransactions.Count,
        TotalDockets = dockets.Count(),
        DockedTransactionCount = dockedTxIds.Count,
        OrphanCount = orphans.Count,
        Orphans = orphans
    });
})
.WithName("DetectOrphanTransactions")
.WithSummary("Detect orphan transactions")
.WithDescription("Finds transactions not referenced by any sealed docket. These are remnants of legacy direct-write paths.")
.RequireAuthorization("CanWriteDockets")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status401Unauthorized);

// <summary>
// Delete orphan transactions (not referenced by any docket)
// </summary>
adminGroup.MapDelete("/orphan-transactions", async (
    IRegisterRepository repository,
    string registerId) =>
{
    var register = await repository.GetRegisterAsync(registerId);
    if (register == null)
        return Results.NotFound(new { error = "Register not found" });

    // Get all dockets and collect their transaction IDs
    var dockets = await repository.GetDocketsAsync(registerId);
    var dockedTxIds = new HashSet<string>(
        dockets.SelectMany(d => d.TransactionIds ?? []),
        StringComparer.OrdinalIgnoreCase);

    // Get all transactions
    var allTxQueryable = await repository.GetTransactionsAsync(registerId);
    var allTransactions = allTxQueryable.ToList();

    // Find orphans
    var orphanTxIds = allTransactions
        .Where(tx => !dockedTxIds.Contains(tx.TxId))
        .Select(tx => tx.TxId)
        .ToList();

    if (orphanTxIds.Count == 0)
        return Results.Ok(new { RegisterId = registerId, DeletedCount = 0, Message = "No orphan transactions found" });

    // Safety check: ensure no other transactions chain from orphans
    var chainedFromOrphans = allTransactions
        .Where(tx => tx.PrevTxId != null && orphanTxIds.Contains(tx.PrevTxId) && !orphanTxIds.Contains(tx.TxId))
        .Select(tx => new { tx.TxId, tx.PrevTxId })
        .ToList();

    if (chainedFromOrphans.Count > 0)
    {
        return Results.Conflict(new
        {
            error = "Cannot delete orphans — some docketed transactions chain from orphan PrevTxIds",
            ChainedTransactions = chainedFromOrphans,
            OrphanTxIds = orphanTxIds
        });
    }

    // Delete each orphan via DeleteTransactionAsync
    var deletedCount = 0;
    foreach (var txId in orphanTxIds)
    {
        try
        {
            await repository.DeleteTransactionAsync(registerId, txId);
            deletedCount++;
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to delete orphan transaction {TxId}", txId);
        }
    }

    return Results.Ok(new
    {
        RegisterId = registerId,
        DeletedCount = deletedCount,
        OrphanTxIds = orphanTxIds
    });
})
.WithName("DeleteOrphanTransactions")
.WithSummary("Delete orphan transactions")
.WithDescription("Removes transactions not referenced by any sealed docket. Refuses if docketed transactions chain from orphans.")
.RequireAuthorization("CanWriteDockets")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status409Conflict)
.Produces(StatusCodes.Status401Unauthorized);

// Local function: builds a Merkle proof path (sibling hashes) for a target transaction
List<string> BuildMerkleProofPath(List<string> txIds, string targetTxId, IHashProvider hashProvider)
{
    if (txIds.Count <= 1)
        return [];

    var proofPath = new List<string>();
    var currentLevel = txIds.Select(h => h.ToLowerInvariant()).ToList();
    int targetIdx = currentLevel.FindIndex(h => string.Equals(h, targetTxId, StringComparison.OrdinalIgnoreCase));
    if (targetIdx < 0)
        return [];

    while (currentLevel.Count > 1)
    {
        var nextLevel = new List<string>();
        int nextTargetIdx = targetIdx / 2;

        for (int i = 0; i < currentLevel.Count; i += 2)
        {
            string left = currentLevel[i];
            string right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;

            // If target is in this pair, add sibling to proof path
            if (i == targetIdx || i + 1 == targetIdx)
            {
                proofPath.Add(i == targetIdx ? right : left);
            }

            // Compute parent hash (matches MerkleTree.CombineAndHash)
            string combined = left + right;
            byte[] combinedBytes = System.Text.Encoding.UTF8.GetBytes(combined);
            byte[] hash = hashProvider.ComputeHash(combinedBytes, Sorcha.Cryptography.Enums.HashType.SHA256);
            nextLevel.Add(Convert.ToHexString(hash).ToLowerInvariant());
        }

        currentLevel = nextLevel;
        targetIdx = nextTargetIdx;
    }

    return proofPath;
}

// Feature 047: Bloom filter admin endpoint (US1)
// <summary>Trigger a full rebuild of the bloom filter for a register.</summary>
adminGroup.MapPost("/rebuild-index", async (
    Sorcha.Register.Service.Services.Interfaces.ILocalAddressIndex addressIndex,
    Sorcha.ServiceClients.Grpc.IWalletNotificationClient walletClient,
    ILogger<Program> logger,
    string registerId) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();

    var addresses = walletClient.GetAllLocalAddressesAsync(registerId, activeOnly: true);
    var addressStream = ExtractAddressStrings(addresses);

    var stats = await addressIndex.RebuildAsync(registerId, addressStream);

    sw.Stop();
    logger.LogInformation(
        "Admin bloom filter rebuild for register {RegisterId}: {AddressCount} addresses in {Duration}ms",
        registerId, stats.AddressCount, sw.ElapsedMilliseconds);

    return Results.Ok(new
    {
        success = true,
        registerId,
        addressCount = stats.AddressCount,
        rebuildDurationMs = sw.ElapsedMilliseconds,
        bitArraySize = stats.BitArraySize,
        hashFunctionCount = stats.HashFunctionCount
    });
})
.WithName("RebuildAddressIndex")
.WithSummary("Rebuild bloom filter address index")
.WithDescription("Triggers a full rebuild of the bloom filter for a register. Fetches all wallet addresses from Wallet Service and rebuilds the Redis-backed probabilistic index. Returns address count and rebuild duration.")
.Produces<object>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized);

static async IAsyncEnumerable<string> ExtractAddressStrings(IAsyncEnumerable<Sorcha.Wallet.Service.Grpc.LocalAddressEntry> entries)
{
    await foreach (var entry in entries)
    {
        yield return entry.Address;
    }
}

// ===========================
// Statistics Endpoint (public, no auth)
// ===========================

app.MapGet("/api/stats", async (
    IRegisterRepository repository,
    string? registerIds) =>
{
    try
    {
        // Feature 131 / UX-005 — optional ?registerIds=a,b,c filter. When set,
        // counts are scoped to the listed registers; this lets Tenant Service
        // build org-scoped dashboard stats by passing the org's subscribed
        // register ids.
        if (!string.IsNullOrWhiteSpace(registerIds))
        {
            var listed = registerIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(50)
                .ToArray();
            var listedTransactionCount = 0;
            foreach (var id in listed)
            {
                listedTransactionCount += (int)await repository.CountTransactionsAsync(id);
            }
            return Results.Ok(new
            {
                registerCount = listed.Length,
                transactionCount = listedTransactionCount
            });
        }

        var registerCount = await repository.CountRegistersAsync();

        // Sum docket heights across all registers as a transaction count proxy
        var registers = await repository.GetRegistersAsync();
        var transactionCount = 0;
        foreach (var register in registers)
        {
            transactionCount += (int)await repository.CountTransactionsAsync(register.Id);
        }

        return Results.Ok(new
        {
            registerCount,
            transactionCount
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to get register statistics");
        return Results.Ok(new
        {
            registerCount = 0,
            transactionCount = 0
        });
    }
})
.WithName("GetRegisterStats")
.WithSummary("Get register statistics (public)")
.WithDescription("Returns aggregate counts of registers and transactions. Optional ?registerIds=a,b,c (comma-separated, max 50) filters the counts to the listed registers. No authentication required.")
.WithTags("Statistics")
.AllowAnonymous();

app.Run();

// ===========================
// Request/Response Models
// ===========================

record CreateRegisterRequest(
    string Name,
    string TenantId,
    bool Advertise = false,
    bool IsFullReplica = true);

record UpdateRegisterRequest(
    [property: StringLength(200)] string? Name = null,
    RegisterStatus? Status = null,
    bool? Advertise = null);

record PublishBlueprintToRegisterRequest(
    [property: Required(AllowEmptyStrings = false), StringLength(200)] string BlueprintId,
    [property: Required(AllowEmptyStrings = false), StringLength(5_000_000)] string BlueprintJson,
    [property: Required(AllowEmptyStrings = false), StringLength(200)] string PublishedBy);

record GovernanceProposalRequest(
    GovernanceOperationType OperationType,
    [property: Required(AllowEmptyStrings = false), StringLength(500)] string ProposerDid,
    [property: Required(AllowEmptyStrings = false), StringLength(500)] string TargetDid,
    RegisterRole? TargetRole = null,
    [property: StringLength(2000)] string? Justification = null,
    List<ApprovalSignature>? ApprovalSignatures = null,
    ValidatorRosterEntry? ValidatorEntry = null);

record WriteDocketRequest(
    [property: Required(AllowEmptyStrings = false), StringLength(256)] string DocketId,
    [property: Range(0, long.MaxValue)] long DocketNumber,
    string? PreviousHash,
    [property: Required(AllowEmptyStrings = false), StringLength(256)] string DocketHash,
    DateTimeOffset CreatedAt,
    List<string> TransactionIds,
    string ProposerValidatorId,
    string MerkleRoot,
    List<TransactionModel>? Transactions = null,
    // Feature 187 (#1371): quorum evidence. Empty in single-validator mode, which is valid.
    List<Sorcha.Register.Models.ConsensusVote>? Votes = null);

// ZK Proof request models
record InclusionProofRequest(
    [property: Required(AllowEmptyStrings = false), StringLength(200)] string TxId,
    [property: Required(AllowEmptyStrings = false), StringLength(200)] string DocketId);
record VerifyInclusionProofRequest(
    [property: Required(AllowEmptyStrings = false), StringLength(200)] string DocketId,
    [property: Required(AllowEmptyStrings = false), StringLength(2048)] string MerkleRoot,
    [property: Required(AllowEmptyStrings = false), StringLength(2048)] string Commitment,
    [property: Required(AllowEmptyStrings = false), StringLength(1_000_000)] string ProofData,
    string[] MerkleProofPath,
    [property: Required(AllowEmptyStrings = false), StringLength(8192)] string VerificationKey);

// Receipt request models
record BatchReceiptRequest(
    [property: Range(0, long.MaxValue)] long DocketNumber,
    [property: Required] TransactionReceipt[] Receipts);
record VerifyReceiptRequest(
    [property: Required] TransactionReceipt Receipt,
    [property: Required(AllowEmptyStrings = false), StringLength(8192)] string ValidatorPublicKey);

// Transaction Graph DTOs (T021 — DAG visualization)
record TransactionGraphNodeDto(
    string TxId,
    string PrevTxId,
    string SenderWallet,
    DateTime TimeStamp,
    ulong? DocketNumber,
    string? BlueprintId,
    string? InstanceId,
    int? TransactionType);

record TransactionGraphResponse(
    string RegisterId,
    TransactionGraphNodeDto[] Nodes,
    int TotalCount,
    bool HasMore);
