// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using System.Buffers.Text;
using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.JsonLd;
using Microsoft.AspNetCore.SignalR;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Schemas.Services;
using Sorcha.Cryptography.Core;
using Sorcha.ServiceClients.Extensions;
using Sorcha.Register.Storage.Redis;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using Sorcha.ServiceClients.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add Redis output caching
builder.AddRedisOutputCache("redis");

// Add Redis client for direct access (blueprint cache population for Validator)
builder.AddRedisClient("redis");

// Add Redis distributed cache for IDistributedCache dependency
builder.AddRedisDistributedCache("redis");

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Blueprint Service API",
    "Blueprint workflow management, action execution, credential lifecycle, schema library, and SignalR real-time notifications.");

// Add storage — EF Core + PostgreSQL when configured, InMemory fallback otherwise.
// SorchaConnections cascade: ConnectionStrings:Blueprint:Postgres → ConnectionStrings:Sorcha:Postgres.
var hasBlueprintPgConfig =
    !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:Blueprint:Postgres"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:Sorcha:Postgres"]);
var blueprintDbConn = hasBlueprintPgConfig
    ? builder.Configuration.GetSorchaPostgresConnectionString("Blueprint", "sorcha_blueprint")
    : null;
var storageLog = builder.Services.GetStorageRegistrationLog();
// IBlueprintStore is a cache that rebuilds from the register transaction log on cold start
// (see BlueprintRecoveryService below). It logs warn-on-fallback but is not on the audited list,
// so an in-memory implementation does not gate Production startup.
if (!string.IsNullOrEmpty(blueprintDbConn))
{
    builder.Services.AddDbContextFactory<Sorcha.Blueprint.Service.Data.BlueprintDbContext>(options =>
        options.UseNpgsql(blueprintDbConn));
    builder.Services.AddSingleton<IBlueprintStore, Sorcha.Blueprint.Service.Storage.EfCoreBlueprintStore>();
    storageLog.RegisterPersistent(
        typeof(IBlueprintStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCoreBlueprintStore).FullName!,
        "postgres");
    // IDocumentStore<BlueprintTemplate, string> shares this branch's connection but is a
    // generic abstraction (Sorcha.Storage.Abstractions), not a named audited interface —
    // intentionally not logged.
    builder.Services.AddSingleton<Sorcha.Storage.Abstractions.IDocumentStore<Sorcha.Blueprint.Models.BlueprintTemplate, string>,
        Sorcha.Blueprint.Service.Storage.EfCoreTemplateStore>();
}
else
{
    builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
    storageLog.RegisterInMemory(
        typeof(IBlueprintStore).FullName!,
        typeof(InMemoryBlueprintStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres — IBlueprintStore is a cache reconstructable from the register transaction log on cold start");
    // IDocumentStore<BlueprintTemplate, string> shares this branch but is a generic
    // abstraction, not a named audited interface — intentionally not logged.
    builder.Services.AddSingleton<Sorcha.Storage.Abstractions.IDocumentStore<Sorcha.Blueprint.Models.BlueprintTemplate, string>>(
        new Sorcha.Storage.InMemory.InMemoryDocumentStore<Sorcha.Blueprint.Models.BlueprintTemplate, string>(t => t.Id));
}
// Published blueprints: InMemory for now — register is the source of truth,
// so published data is reconstructable. Redis cache (068 US3) deferred to follow-up.
builder.Services.AddSingleton<IPublishedBlueprintStore, InMemoryPublishedBlueprintStore>();
storageLog.RegisterInMemory(
    typeof(IPublishedBlueprintStore).FullName!,
    typeof(InMemoryPublishedBlueprintStore).FullName!,
    "register transaction log is the source of truth — published data reconstructable on cold start (Redis cache deferred to feature 068 US3)");

// Recovery: rebuild published blueprint state from register ledger on startup
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Models.RecoveryState>();
builder.Services.Configure<Sorcha.Blueprint.Service.Models.RecoveryOptions>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Models.RecoveryOptions.SectionName));
builder.Services.AddHttpClient("RegisterService", client =>
{
    var address = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Register) ?? "http://register-service:5290";
    client.BaseAddress = new Uri(address);
});
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.BlueprintRecoveryService>();

// Add Blueprint services
builder.Services.AddScoped<IBlueprintService, BlueprintService>();
builder.Services.AddScoped<IPublishService, PublishService>();
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Interfaces.IJsonEEvaluator, Sorcha.Blueprint.Engine.Implementation.JsonEEvaluator>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService, Sorcha.Blueprint.Service.Templates.BlueprintTemplateService>();

// Add Cryptography services (required for transaction building)
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.ICryptoModule, Sorcha.Cryptography.Core.CryptoModule>();
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.IHashProvider, Sorcha.Cryptography.Core.HashProvider>();
builder.Services.AddScoped<Sorcha.Cryptography.Interfaces.ISymmetricCrypto, Sorcha.Cryptography.Core.SymmetricCrypto>();

// Add Encryption pipeline services (045-encrypted-payload-integration)
builder.Services.AddScoped<Sorcha.TransactionHandler.Encryption.IEncryptionPipelineService, Sorcha.TransactionHandler.Encryption.EncryptionPipelineService>();
builder.Services.AddSingleton<Sorcha.TransactionHandler.Encryption.IDisclosureGroupBuilder, Sorcha.TransactionHandler.Encryption.DisclosureGroupBuilder>();

// Encryption async pipeline - background processing with SignalR notifications (045 Phase 7)
builder.Services.AddSingleton(System.Threading.Channels.Channel.CreateBounded<Sorcha.Blueprint.Service.Models.EncryptionWorkItem>(
    new System.Threading.Channels.BoundedChannelOptions(100)
    {
        FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
    }));
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.IEncryptionOperationStore,
    Sorcha.Blueprint.Service.Services.Implementation.InMemoryEncryptionOperationStore>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.EncryptionBackgroundService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Implementation.IEncryptionInboxWriter,
    Sorcha.Blueprint.Service.Services.Implementation.EncryptionInboxWriter>();

// Add transaction confirmation options
builder.Services.Configure<Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Models.TransactionConfirmationOptions.SectionName));

// Add Execution Engine services (Sprint 5)
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Caching.JsonSchemaCache>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.ISchemaValidator, Sorcha.Blueprint.Engine.Implementation.SchemaValidator>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IJsonLogicEvaluator, Sorcha.Blueprint.Engine.Implementation.JsonLogicEvaluator>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IDisclosureProcessor, Sorcha.Blueprint.Engine.Implementation.DisclosureProcessor>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IRoutingEngine, Sorcha.Blueprint.Engine.Implementation.RoutingEngine>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ICredentialIssuer, Sorcha.Blueprint.Engine.Credentials.CredentialIssuer>();
builder.Services.AddHttpClient<Sorcha.Blueprint.Engine.Credentials.IRevocationChecker, Sorcha.Blueprint.Engine.Credentials.BitstringStatusListChecker>();

// Feature 135 — unified credential trust. The CredentialVerifier now dispatches to a
// per-format ICredentialFormatHandler that verifies the signature for real and routes the
// trust decision through the single ITrustEvaluator (no SignatureValid=false shortcut).
// Network trust sources live behind engine-local seams with service-layer adapters here,
// keeping Sorcha.Blueprint.Engine WASM-friendly.
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory,
    Sorcha.Blueprint.Service.Credentials.DidIssuerDirectory>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.IIssuerKeyResolver,
    Sorcha.Blueprint.Service.Credentials.DidX5cIssuerKeyResolver>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.RegisterTrustSourceResolver(
        sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory>()));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.DidAllowlistTrustSourceResolver(
        sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.IIssuerDirectory>()));
// Feature 181 US3 — trustlist trust source over imported ETSI TS 119 612 snapshots. The caching
// HTTP provider reads the Tenant anchors endpoint; TrustListAnchorProvider carries the snapshot
// identity ({trustListId}#{seq}) into TrustEvidence.TrustListId.
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.ITrustListProvider,
    Sorcha.ServiceClients.Trust.HttpTrustListProvider>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.Sources.TrustListSourceResolver(
        new Sorcha.Blueprint.Service.Credentials.TrustListAnchorProvider(
            sp.GetRequiredService<Sorcha.ServiceClients.Trust.ITrustListProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.Blueprint.Service.Credentials.TrustListAnchorProvider>>())));
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustResolverRegistry>(sp =>
    new Sorcha.Blueprint.Engine.Credentials.TrustResolverRegistry(
        sp.GetServices<Sorcha.Blueprint.Engine.Credentials.ITrustSourceResolver>()));
// BitstringStatusListChecker implements both IRevocationChecker and IStatusListChecker.
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.IStatusListChecker>(sp =>
    (Sorcha.Blueprint.Engine.Credentials.IStatusListChecker)sp.GetRequiredService<Sorcha.Blueprint.Engine.Credentials.IRevocationChecker>());
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ITrustEvaluator,
    Sorcha.Blueprint.Engine.Credentials.TrustEvaluator>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ICredentialFormatHandler,
    Sorcha.Blueprint.Engine.Credentials.SdJwtVcFormatHandler>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Credentials.ICredentialVerifier,
    Sorcha.Blueprint.Engine.Credentials.CredentialVerifier>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IActionProcessor, Sorcha.Blueprint.Engine.Implementation.ActionProcessor>();
builder.Services.AddScoped<Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine, Sorcha.Blueprint.Engine.Implementation.ExecutionEngine>();

// Add SD-JWT service for credential verification
builder.Services.AddSingleton<Sorcha.Cryptography.SdJwt.ISdJwtService, Sorcha.Cryptography.SdJwt.SdJwtService>();

// Add JsonLogic expression cache (singleton - shared across scoped evaluators)
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Caching.JsonLogicCache>();

// File upload session store for chunk encryption key continuity (Feature 085)
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Endpoints.FileUploadSessionStore>();

// Add Action service layer (Sprint 3)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IActionResolverService, Sorcha.Blueprint.Service.Services.Implementation.ActionResolverService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IPayloadResolverService, Sorcha.Blueprint.Service.Services.Implementation.PayloadResolverService>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService, Sorcha.Blueprint.Service.Services.Implementation.TransactionBuilderService>();

// Add consolidated service clients (Sprint 6)
builder.Services.AddServiceClients(builder.Configuration);

// Add Action / Instance storage — EF Core when configured, InMemory fallback.
// Both IActionStore and IInstanceStore are audited interfaces — Production/Staging
// fail-fast when on in-memory.
if (!string.IsNullOrEmpty(blueprintDbConn))
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IActionStore, Sorcha.Blueprint.Service.Storage.EfCoreActionStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Service.Storage.IActionStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCoreActionStore).FullName!,
        "postgres");

    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IInstanceStore, Sorcha.Blueprint.Service.Storage.EfCoreInstanceStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Service.Storage.IInstanceStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCoreInstanceStore).FullName!,
        "postgres");
}
else
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IActionStore, Sorcha.Blueprint.Service.Storage.InMemoryActionStore>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Service.Storage.IActionStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.InMemoryActionStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres");

    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IInstanceStore, Sorcha.Blueprint.Service.Storage.InMemoryInstanceStore>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Service.Storage.IInstanceStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.InMemoryInstanceStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres");
}

// Feature 142 — RehearsalPass + PublishOverride storage (EF Core when configured,
// InMemory fallback). Convenience-grade: NOT on the F113 fail-fast audited list, so
// these warn on in-memory but do not gate startup.
if (!string.IsNullOrEmpty(blueprintDbConn))
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IRehearsalPassStore, Sorcha.Blueprint.Service.Storage.EfCoreRehearsalPassStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Service.Storage.IRehearsalPassStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCoreRehearsalPassStore).FullName!,
        "postgres");

    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IPublishOverrideStore, Sorcha.Blueprint.Service.Storage.EfCorePublishOverrideStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Service.Storage.IPublishOverrideStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCorePublishOverrideStore).FullName!,
        "postgres");

    // Status lists hold revocation state. Unlike the two above this is NOT convenience-grade: a
    // revocation is meant to be permanent and publicly checkable, and losing it silently un-revokes
    // credentials for anyone who checks (#1482).
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IStatusListStore, Sorcha.Blueprint.Service.Storage.EfCoreStatusListStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Service.Storage.IStatusListStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.EfCoreStatusListStore).FullName!,
        "postgres");
}
else
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IRehearsalPassStore, Sorcha.Blueprint.Service.Storage.InMemoryRehearsalPassStore>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Service.Storage.IRehearsalPassStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.InMemoryRehearsalPassStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres — convenience-grade, not audited");

    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IPublishOverrideStore, Sorcha.Blueprint.Service.Storage.InMemoryPublishOverrideStore>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Service.Storage.IPublishOverrideStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.InMemoryPublishOverrideStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres — convenience-grade, not audited");

    // This is the pre-#1482 behaviour, kept only as the no-database fallback. It is registered as
    // in-memory deliberately so the storage audit reports it rather than it looking durable.
    builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.IStatusListStore, Sorcha.Blueprint.Service.Storage.InMemoryStatusListStore>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Service.Storage.IStatusListStore).FullName!,
        typeof(Sorcha.Blueprint.Service.Storage.InMemoryStatusListStore).FullName!,
        "no Postgres connection string in ConnectionStrings:Blueprint:Postgres or ConnectionStrings:Sorcha:Postgres — revocation state will NOT survive a restart");
}

// Add Orchestration services (Sprint 6)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IStateReconstructionService,
    Sorcha.Blueprint.Service.Services.Implementation.StateReconstructionService>();
// Feature 176 — shared disclosure authority. Backs the execution path's submit-side disclosure and the
// disclosed-data query endpoint's read-side reconstruction from one implementation.
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IActionDisclosureResolver,
    Sorcha.Blueprint.Service.Services.Implementation.ActionDisclosureResolver>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService,
    Sorcha.Blueprint.Service.Services.Implementation.ActionExecutionService>();
// Feature 145 US6 — ActionExecutionService also builds the signed RoutingDecision a successful
// presentation outcome carries (so the projector advances on its seal). Forward the interface to the
// same scoped instance.
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationRoutingDecisionBuilder>(
    sp => (Sorcha.Blueprint.Service.Services.Interfaces.IPresentationRoutingDecisionBuilder)
        sp.GetRequiredService<Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService>());

// Feature 142 (US2) — full-rehearsal orchestration. The sandbox-register provider and the
// orchestration service hold process-wide transient state (per-org sandbox-register cache;
// in-flight rehearsal sessions) so both are singletons; the orchestration service resolves its
// scoped collaborators (execution pipeline, stores, service clients) per operation via
// IServiceScopeFactory. The executable-definition hasher is a stateless POCO.
builder.Services.AddSingleton<Sorcha.Blueprint.Engine.Implementation.ExecutableDefinitionHasher>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.ISandboxRegisterProvider,
    Sorcha.Blueprint.Service.Services.Implementation.SandboxRegisterProvider>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.IRehearsalOrchestrationService,
    Sorcha.Blueprint.Service.Services.Implementation.RehearsalOrchestrationService>();

// Feature 142 (T037/T038 / FR-027 + FR-032) — server-side publish gate. Scoped because it
// depends on the scoped IRegisterServiceClient (governance roster read); reads the rehearsal-pass
// store and computes the exec-def hash to evaluate the governance-hard + rehearsal-soft gates.
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Implementation.IPublishGate,
    Sorcha.Blueprint.Service.Services.Implementation.PublishGate>();

// Feature 111: Timebound Presentation Lifecycle — Redis-backed transient state and rate limiting.
builder.Services.Configure<Sorcha.Blueprint.Service.Configuration.PresentationLifecycleOptions>(
    builder.Configuration.GetSection("PresentationLifecycle"));

// Multi-node audit CRITICAL #3 — fail-closed wallet ownership validation.
builder.Services.Configure<Sorcha.Blueprint.Service.Configuration.WalletOwnershipSettings>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Configuration.WalletOwnershipSettings.SectionName));
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.Presentations.IPendingPresentationStore,
    Sorcha.Blueprint.Service.Storage.Presentations.RedisPendingPresentationStore>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.Presentations.IPresentationRateLimiter,
    Sorcha.Blueprint.Service.Storage.Presentations.RedisPresentationRateLimiter>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationLifecycleService,
    Sorcha.Blueprint.Service.Services.Implementation.PresentationLifecycleService>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Infrastructure.IClock,
    Sorcha.Blueprint.Service.Services.Infrastructure.SystemClock>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Implementation.PresentationLifecycleMetrics>();

// Feature 142 — Blueprint Design Lifecycle designer metrics (instruments added in T058).
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Implementation.BlueprintDesignerMetrics>();

// Feature 111 — IPresentationConsumer registrations dispatched by name from
// PresentationLifecycleService. Consumers run in-process here in Blueprint
// Service because the lifecycle dispatcher resolves them from the local DI
// container; they cannot live in their originating service's process.
builder.Services.AddSingleton<Sorcha.PresentationLifecycle.Abstractions.IPresentationConsumer,
    Sorcha.Blueprint.Service.Services.Implementation.HaipPresentationConsumer>();

// Feature 127 — Sorcha.Verifier.Engine dependencies the SorchaWalletPresentationConsumer
// consumes. Production issuer-key resolution lands here (F120 → Blueprint Service):
// the council-page credential gate verifies citizen-presented credentials against
// the issuer's published DID document via DidResolverBackedIssuerKeyResolver, with
// the JWK-registry resolver as a fallback for dev/demo flows that mint per-test
// issuer keys without publishing a DID document. Verifier-DID resolution (the
// client_id placeholder in SorchaWalletPresentationConsumer.BuildInitiationAsync)
// is separate and still lands in Spec 5.
builder.Services.AddHttpClient<Sorcha.Verifier.Engine.IStatusListCache,
    Sorcha.Verifier.Engine.StatusListCache>();
builder.Services.TryAddSingleton(TimeProvider.System);

// Feature 138 US1 — the council-gate verifier authenticates status lists against the issuer's
// sealed-state key and fails closed. Metrics record rejections (FR-022); the configured clock skew
// bounds freshness tolerance. The StatusListCache auto-injects the metrics via ActivatorUtilities.
builder.Services.AddSingleton<Sorcha.Verifier.Engine.FederationVerifierMetrics>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Implementation.FederationBlueprintMetrics>();
var f138ClockSkew = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int?>("Verifier:ClockSkewSeconds") ?? 60);
var f138KbJwtMaxLifetime = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int?>("Verifier:KbJwtMaxLifetimeSeconds") ?? 120);

// Feature 120 — DID resolver infrastructure (cache, OTel meters, registry, did:sorcha
// + did:web + did:key built-ins). Idempotent; safe even if a transitive dependency
// has already registered the same components.
Sorcha.ServiceClients.Http.Extensions.HttpServiceCollectionExtensions
    .AddDidResolvers(builder.Services, builder.Configuration);

// Feature 149 — the engine verifier must resolve an org's PUBLISHED did.json (the only place
// the re-anchored issuer DID's vc-issuance key lives — the issuer DID is the operational wallet
// A, but credentials are signed by the derived sub-key C), NOT rebuild it from the wallet row.
// Override SorchaDidResolver to the published-DID-aware ctor pointed at the Tenant by-DID route.
// Registered AFTER AddDidResolvers so this scoped factory wins; the IDidResolver delegate (and
// thus the registry) resolves this instance.
builder.Services.AddHttpClient("PublishedOrgDid", client =>
{
    client.BaseAddress = new Uri(
        SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
        ?? "http://tenant-service:8080");
});
builder.Services.AddScoped<Sorcha.ServiceClients.Did.SorchaDidResolver>(sp =>
    new Sorcha.ServiceClients.Did.SorchaDidResolver(
        sp.GetRequiredService<Sorcha.ServiceClients.Wallet.IWalletServiceClient>(),
        sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("PublishedOrgDid"),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.ServiceClients.Did.SorchaDidResolver>>()));

// DidResolverBackedIssuerKeyResolver consumes the scoped IDidResolverRegistry, so it (and the
// composite resolver + validator that depend on it) MUST be scoped — registering them as singletons
// is a captive dependency that production DI validation (ValidateOnBuild) rejects at startup. Mirrors
// the reference verifier's wiring (Sorcha.Verifier/Extensions/ServiceCollectionExtensions.cs, fixed in
// #810). JwkRegistryIssuerKeyResolver holds no scoped dependency, so it stays a singleton.
builder.Services.AddSingleton<Sorcha.Verifier.Engine.JwkRegistryIssuerKeyResolver>();
builder.Services.AddScoped<Sorcha.Verifier.Engine.DidResolverBackedIssuerKeyResolver>();
builder.Services.AddScoped<Sorcha.Verifier.Engine.IIssuerKeyResolver>(sp =>
    new Sorcha.Verifier.Engine.CompositeIssuerKeyResolver(
    [
        sp.GetRequiredService<Sorcha.Verifier.Engine.DidResolverBackedIssuerKeyResolver>(),
        sp.GetRequiredService<Sorcha.Verifier.Engine.JwkRegistryIssuerKeyResolver>()
    ]));
builder.Services.AddScoped<Sorcha.Verifier.Engine.IVerifiablePresentationValidator>(sp =>
    new Sorcha.Verifier.Engine.VerifiablePresentationValidator(
        sp.GetRequiredService<Sorcha.Verifier.Engine.IStatusListCache>(),
        sp.GetRequiredService<Sorcha.Verifier.Engine.IIssuerKeyResolver>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ILogger<Sorcha.Verifier.Engine.VerifiablePresentationValidator>>(),
        requireIssuerSignature: builder.Configuration.GetValue<bool?>("IssuerSignature:Required") ?? true,
        metrics: sp.GetService<Sorcha.Verifier.Engine.FederationVerifierMetrics>(),
        clockSkew: f138ClockSkew,
        kbJwtMaxLifetime: f138KbJwtMaxLifetime));

// Feature 127 — Sorcha-wallet consumer. Verifies SD-JWT presentations posted
// by the citizen's Sorcha wallet via Sorcha.Verifier.Engine. The first
// non-HAIP IPresentationConsumer, implementing the new BuildInitiationAsync
// extension on the consumer contract.
// Scoped — it consumes the scoped IVerifiablePresentationValidator. The Scoped
// PresentationLifecycleService resolves the IPresentationConsumer collection, so a scoped
// consumer is valid alongside the singleton HaipPresentationConsumer above.
builder.Services.AddScoped<Sorcha.PresentationLifecycle.Abstractions.IPresentationConsumer,
    Sorcha.Blueprint.Service.Services.Implementation.SorchaWalletPresentationConsumer>();

// Spec 5 — verifier-DID resolution. The lifecycle service resolves the council
// org's canonical DID (blueprint.OrganizationId → GET /orgs/{id}/did.json) so the
// OID4VP client_id carries a real verifier identity instead of did:sorcha:org:UNKNOWN.
// Same Tenant base-address pattern as Wallet Service's F120 registration.
builder.Services.AddHttpClient<Sorcha.ServiceClients.OrgDidDocument.IOrgDidDocumentClient,
    Sorcha.ServiceClients.OrgDidDocument.OrgDidDocumentClient>(client =>
    {
        client.BaseAddress = new Uri(
            SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
            ?? "http://tenant-service:8080");
    });

// Feature 181 US3 — named client for the caching trust-list anchor provider (service-tier read of
// the Tenant trusted-list anchors endpoint).
builder.Services.AddHttpClient(Sorcha.ServiceClients.Trust.HttpTrustListProvider.HttpClientName, client =>
{
    client.BaseAddress = new Uri(
        SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Tenant)
        ?? "http://tenant-service:8080");
});

// Feature 127 — single-use ClaimsFetchToken store. Minted by InitiateAsync
// (for Sorcha-wallet only); consumed atomically by the disclosed-claims
// endpoint. Backed by Redis via the existing IConnectionMultiplexer the
// F111 stores already share.
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.Presentations.IClaimsFetchTokenStore,
    Sorcha.Blueprint.Service.Storage.Presentations.RedisClaimsFetchTokenStore>();

// Feature 181 (T014) — served OpenID4VP request objects for the sorcha-wallet
// consumer's request_uri deep-link form. Same Redis multiplexer as the F111 stores.
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.Presentations.IRequestObjectStore,
    Sorcha.Blueprint.Service.Storage.Presentations.RedisRequestObjectStore>();

// Feature 127 — short-TTL plaintext stash of disclosed claims, written
// alongside the outcome tx for the disclosed-claims endpoint to read.
// Avoids re-decrypting the register tx on every council-page fetch.
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Storage.Presentations.IDisclosedClaimsStore,
    Sorcha.Blueprint.Service.Storage.Presentations.RedisDisclosedClaimsStore>();

builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.AbandonmentSweeper>();

// Feature 119 — seal-aware ordering for chain-pointer-bearing presentation lifecycle
// transactions. Singleton coordinator + BackgroundService subscriber on the existing
// transaction:confirmed Redis Streams channel. See:
//   docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md
//   specs/119-presentation-seal-ordering/spec.md
// CRITICAL: Blueprint Service MUST consume register events under its OWN consumer
// group. Redis Streams consumer groups are competing-consumer: every service that
// joins the same group name on a stream shares the messages (each delivered to ONE
// member). The shared default "register-service" made Blueprint's reconstructor /
// presentation-seal subscriber COMPETE with the Register Service's own SignalR
// bridge on docket:confirmed + transaction:confirmed — so a docket:confirmed event
// landed on the reconstructor only ~half the time and cross-node instance mirrors
// were never materialised. A distinct group gives Blueprint its own copy of every
// event (Validator Service already does this with "validator-service").
builder.Services.AddRedisEventStreams(config =>
{
    builder.Configuration.GetSection("EventStreams:Redis").Bind(config);
    config.ConsumerGroup = "blueprint-service";
});
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.IPresentationSealCoordinator,
    Sorcha.Blueprint.Service.Services.Implementation.RedisPresentationSealCoordinator>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.PresentationSealSubscriber>();

// Feature 103 US1: Redis read-through cache for per-instance participant bindings.
// Hot-path lookup for Instance.ParticipantWallets during action execution.
// Contract: specs/103-verified-citizen-v2/contracts/instance-binding-cache.md
builder.Services.Configure<Sorcha.Blueprint.Service.Services.InstanceBindingCacheOptions>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Services.InstanceBindingCacheOptions.SectionName));
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.IInstanceBindingCache,
    Sorcha.Blueprint.Service.Services.InstanceBindingCache>();

// Feature 103 US2: Core identity primitive library.
// Seeds Sorcha core schema primitives from blueprints/schemas/sorcha-core/*.json
// at startup so they are resolvable via JSON Schema $ref from consuming blueprints.
// Contract: specs/103-verified-citizen-v2/contracts/identity-primitive-format.md
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.ICoreSchemaRepository,
    Sorcha.Blueprint.Service.Services.InMemoryCoreSchemaRepository>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.CoreSchemaSeedService>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.ISchemaRefResolver,
    Sorcha.Blueprint.Service.Services.SchemaRefResolver>();

// Add Transaction Retrieval service (045 - Phase 9: Recipient Decryption)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.ITransactionRetrievalService,
    Sorcha.Blueprint.Service.Services.Implementation.TransactionRetrievalService>();

// Feature 145 — InstanceProjector: the single deterministic instance projector. Subscribes to
// docket:confirmed on EVERY node holding the register and folds each sealed action transaction
// into the instance materialized view (pure InstanceProjection fold, idempotent on the
// LastAppliedTxId watermark). Replaces the owner-only InstanceMirrorReconstructor and the
// submitter's imperative state mutation — one shared state machine, no origin/mirror split.
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Implementation.InstanceProjectorMetrics>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.InstanceProjector>();

// Feature 145 US2 — ReactionDispatcher: owns the workflow's at-least-once, role-gated side effects
// (notification + durable inbox writes), keeping the projector pure. The projector invokes it
// in-process after folding an instance; the dispatcher entitlement-gates each reaction (only the
// node hosting the target wallet fires it) and idempotency-claims it on (sealedTxId, kind, wallet)
// via IAtomicDistributedCache SET-NX. Credential mint stays inline on the submit path by design.
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Implementation.ReactionDispatcherMetrics>();
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Implementation.IReactionDispatcher,
    Sorcha.Blueprint.Service.Services.Implementation.ReactionDispatcher>();
// IAtomicDistributedCache backs the reaction idempotency claim (SET-NX on (sealedTxId, kind, wallet)).
Sorcha.AtomicCache.Extensions.AtomicCacheServiceExtensions.AddAtomicDistributedCache(
    builder.Services, builder.Configuration, "Blueprint");

// Feature 145 US4 — InstanceRebuildService: reconstructs an instance's control state purely from the
// register's sealed transactions (same InstanceProjectionResolver the projector uses → bit-for-bit
// parity). Backs the parity self-check + an operator-triggered rebuild/repair of the materialized view.
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Implementation.IInstanceRebuildService,
    Sorcha.Blueprint.Service.Services.Implementation.InstanceRebuildService>();

// Orphan chunk cleanup — removes file metadata records with no confirmed parent transaction
builder.Services.Configure<Sorcha.Blueprint.Service.Models.OrphanChunkCleanupOptions>(
    builder.Configuration.GetSection(Sorcha.Blueprint.Service.Models.OrphanChunkCleanupOptions.SectionName));
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.OrphanChunkCleanupService>();

// Feature 118 — multi-node hub fan-out via Redis backplane (US1).
// AddSorchaHub wires JWT auth + Redis backplane (ChannelPrefix=sorcha:signalr:blueprint)
// + reconnect-with-jitter + OpenTelemetry instrumentation, identically across services.
// ChatHub is the deliberate exception (FR-005, FR-019) — RPC-streaming wire shape;
// it does not register through AddSorchaHub but still inherits the backplane because
// AddStackExchangeRedis applies to every hub in the service.
builder.Services.AddSorchaHub<BlueprintHub, IBlueprintHubClient>(
    builder.Configuration, "/hubs/blueprint", "blueprint");

// AI tool execution can take 30-60+ seconds per turn with multiple continuation rounds.
// Default 30s client timeout causes disconnects during long AI processing. The settings
// here apply to ChatHub specifically, but HubOptions are global so notification hubs
// inherit them too — that's fine, longer timeouts are conservative.
builder.Services.Configure<HubOptions>(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(3);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

// Add Notification service (Sprint 5)
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.INotificationService,
    Sorcha.Blueprint.Service.Services.Implementation.NotificationService>();

// Feature 118 / US3 follow-up — wire BlueprintInboxWriter so action-available
// notifications also produce durable inbox entries via Tenant Service.
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Implementation.IBlueprintInboxWriter,
    Sorcha.Blueprint.Service.Services.Implementation.BlueprintInboxWriter>();

// Add AI-assisted Blueprint Chat services (Sprint 8)
builder.Services.AddChatServices(builder.Configuration);

// Add Schema Store services (Sprint 7)
builder.Services.AddSingleton<SystemSchemaLoader>();
builder.Services.AddScoped<ISchemaStore, SchemaStore>();

// Add External Schema Providers (multiple sources for schema index)
builder.Services.AddHttpClient<SchemaStoreOrgProvider>(client =>
{
    client.BaseAddress = new Uri("https://www.schemastore.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<SchemaStoreOrgProvider>());

builder.Services.AddHttpClient<SchemaOrgProvider>(client =>
{
    client.BaseAddress = new Uri("https://schema.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<SchemaOrgProvider>());

builder.Services.AddHttpClient<FhirSchemaProvider>(client =>
{
    client.BaseAddress = new Uri("https://hl7.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Sorcha-Blueprint-Service/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(2, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp => sp.GetRequiredService<FhirSchemaProvider>());

// Local schemas — Sorcha's curated defaults from blueprints/schemas/ (UK Address, etc.)
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var schemasPath = LocalSchemaProvider.ResolveSchemasPath(env.ContentRootPath);
    return new LocalSchemaProvider(schemasPath,
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalSchemaProvider>>());
});

// Static providers (no HTTP client needed)
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new W3cVcProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<W3cVcProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new UblSchemaProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<UblSchemaProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new Iso20022Provider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Iso20022Provider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    StaticFileSchemaProvider.CreateNiemProvider(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StaticFileSchemaProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    StaticFileSchemaProvider.CreateIfcProvider(
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StaticFileSchemaProvider>>()));
builder.Services.AddSingleton<IExternalSchemaProvider>(sp =>
    new DppSchemaProvider(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DppSchemaProvider>>()));

// Add Schema Library services (034-schema-library).
// F113 storage gate: only construct the Mongo-backed index when a real Mongo
// connection string is configured. Without one (local dev / infra-free test
// hosts) fall back to an in-memory index — otherwise the SchemaIndexRefreshService
// would call into a MongoClient pointed at a non-existent localhost:27017 and spam
// 30s connection-timeout warnings on every startup/refresh.
// SorchaConnections cascade: ConnectionStrings:Blueprint:Mongo → ConnectionStrings:Sorcha:Mongo.
var schemaIndexMongoConnStr = builder.Configuration["ConnectionStrings:Blueprint:Mongo"]
                           ?? builder.Configuration["ConnectionStrings:Sorcha:Mongo"];
if (!string.IsNullOrWhiteSpace(schemaIndexMongoConnStr))
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Schemas.Repositories.ISchemaIndexRepository>(sp =>
    {
        // Mongo connection strings carry credentials/host only; database is selected via GetDatabase.
        var mongoClient = new MongoDB.Driver.MongoClient(schemaIndexMongoConnStr);
        var database = mongoClient.GetDatabase("sorcha-blueprints");
        var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sorcha.Blueprint.Schemas.Repositories.MongoSchemaIndexRepository>>();
        return new Sorcha.Blueprint.Schemas.Repositories.MongoSchemaIndexRepository(database, logger);
    });
    storageLog.RegisterPersistent(
        typeof(Sorcha.Blueprint.Schemas.Repositories.ISchemaIndexRepository).FullName!,
        typeof(Sorcha.Blueprint.Schemas.Repositories.MongoSchemaIndexRepository).FullName!,
        "mongo");
}
else
{
    builder.Services.AddSingleton<Sorcha.Blueprint.Schemas.Repositories.ISchemaIndexRepository,
        Sorcha.Blueprint.Schemas.Repositories.InMemorySchemaIndexRepository>();
    storageLog.RegisterInMemory(
        typeof(Sorcha.Blueprint.Schemas.Repositories.ISchemaIndexRepository).FullName!,
        typeof(Sorcha.Blueprint.Schemas.Repositories.InMemorySchemaIndexRepository).FullName!,
        "no Mongo connection string in ConnectionStrings:Blueprint:Mongo or ConnectionStrings:Sorcha:Mongo");
}
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.ISchemaIndexService,
    Sorcha.Blueprint.Service.Services.SchemaIndexService>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.SchemaIndexRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Sorcha.Blueprint.Service.Services.SchemaIndexRefreshService>());
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.Interfaces.ISectorFilterService,
    Sorcha.Blueprint.Service.Services.SectorFilterService>();

// Register MongoDB schema repository for persistent schema storage (063 AI Builder)
builder.Services.Configure<Sorcha.Blueprint.Schemas.Repositories.MongoSchemaStorageConfiguration>(options =>
{
    options.ConnectionString = builder.Configuration["ConnectionStrings:Blueprint:Mongo"]
                            ?? builder.Configuration["ConnectionStrings:Sorcha:Mongo"]
                            ?? "mongodb://localhost:27017";
    options.DatabaseName = "sorcha-blueprints";
    options.CollectionName = "schemas";
});
builder.Services.AddScoped<Sorcha.Blueprint.Schemas.Repositories.ISchemaRepository,
    Sorcha.Blueprint.Schemas.Repositories.MongoSchemaRepository>();

// Seed blueprint templates from JSON files on startup (059 US5)
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.TemplateSeedService>();

// Note: SchemaSeedService removed — LocalSchemaProvider now feeds schemas from
// blueprints/schemas/ into the unified schema index, serving both UI and AI tools.

// Add Status List Manager (039-verifiable-presentations)
// Issue #1447: resolve the status-list base URLs ONCE at startup — fails fast in
// Production/Staging when unset, and in every environment on the sorcha.example
// placeholder. The URL is signed into each issued credential and unfixable after.
builder.Services.AddSingleton(
    Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolve(
        builder.Configuration, builder.Environment.EnvironmentName));
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.StatusListLedgerReconciler>();
builder.Services.AddSingleton<Sorcha.Blueprint.Service.Services.IStatusListManager,
    Sorcha.Blueprint.Service.Services.StatusListManager>();

// Feature 095: IETF Token Status List serializer (parallel to W3C)
builder.Services.AddSingleton<IIetfTokenStatusListSerializer, IetfTokenStatusListSerializer>();

// Add JWT authentication and authorization (AUTH-002)
// JWT authentication is now configured via shared ServiceDefaults with auto-key generation
builder.AddJwtAuthentication();
builder.Services.AddBlueprintAuthorization();

// Add CORS policy (SEC-001/SEC-005) — shared policy: gateway-perimeter by default, with an optional
// Cors:AllowedOrigins service-level allow-list for defence-in-depth. See CorsExtensions.
builder.AddSorchaCors();

var app = builder.Build();
var logger = app.Logger;

// Issue #1433 — sanitized global exception handler, FIRST in the pipeline so it wraps every
// other middleware's unhandled exceptions too (see ServiceDefaults.Extensions for rationale).
app.UseSanitizedExceptionHandling();

// Apply database migrations on startup (if PostgreSQL is configured)
if (!string.IsNullOrEmpty(blueprintDbConn))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<Sorcha.Blueprint.Service.Data.BlueprintDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        logger.LogInformation("Applying Blueprint database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Blueprint database migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply Blueprint database migrations — service will continue but durable storage may not work");
    }
}

// Configure the HTTP request pipeline
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure OpenAPI and Scalar API documentation UI
app.MapSorchaOpenApiUi("Sorcha Blueprint Service API");

app.UseOutputCache();

// Enable JSON-LD content negotiation
app.UseJsonLdContentNegotiation();

// Enable CORS (SEC-005)
app.UseCors();

// Add authentication and authorization middleware (AUTH-002)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Add Delegation Token Middleware (Sprint 6 - Orchestration)
app.UseMiddleware<Sorcha.Blueprint.Service.Middleware.DelegationTokenMiddleware>();

// Map SignalR hubs.
// BlueprintHub mapped via MapSorchaHubs from the AddSorchaHub registry above.
// ChatHub is the deliberate exception (FR-005, FR-019), mapped explicitly.
app.MapSorchaHubs();
app.MapHub<Sorcha.Blueprint.Service.Hubs.ChatHub>("/hubs/chat").RequireAuthorization();

// Map Operations endpoints (045 Phase 7 - async encryption status)
app.MapOperationsEndpoints();

// ===========================
// Blueprint CRUD Endpoints
// ===========================

var blueprintGroup = app.MapGroup("/api/blueprints")
    .WithTags("Blueprints")
    .RequireAuthorization("CanManageBlueprints");

// <summary>
// Get all blueprints with pagination
// Supports JSON-LD via Accept: application/ld+json header
// </summary>
blueprintGroup.MapGet("/", async (
    HttpContext context,
    IBlueprintService service,
    int page = 1,
    int pageSize = 20,
    string? search = null,
    string? status = null) =>
{
    // Service tokens see all blueprints; user tokens are org-scoped
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var blueprints = await service.GetAllAsync(page, pageSize, search, status, orgId);
    return Results.Ok(blueprints);
})
.WithName("GetBlueprints")
.WithSummary("Get all blueprints")
.WithDescription("Retrieve a paginated list of blueprints with optional search and status filtering. Supports JSON-LD via Accept: application/ld+json header.")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));

// <summary>
// Get blueprint by ID
// Supports JSON-LD via Accept: application/ld+json header
// </summary>
blueprintGroup.MapGet("/{id}", async (HttpContext context, string id, IBlueprintService service) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var blueprint = await service.GetByIdAsync(id, orgId);
    if (blueprint is null) return Results.NotFound();

    // Add JSON-LD context if requested
    if (context.AcceptsJsonLd())
    {
        blueprint = JsonLdHelper.EnsureJsonLdContext(blueprint);
    }

    return Results.Ok(blueprint);
})
.WithName("GetBlueprintById")
.WithSummary("Get blueprint by ID")
.WithDescription("Retrieve a specific blueprint by its unique identifier. Supports JSON-LD via Accept: application/ld+json header.")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag("blueprints"));

// <summary>
// Create new blueprint
// Supports JSON-LD via Accept: application/ld+json header
// </summary>
blueprintGroup.MapPost("/", async (
    HttpContext context,
    BlueprintModel blueprint,
    IBlueprintService service,
    IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var created = await service.CreateAsync(blueprint, orgId);
    await cache.EvictByTagAsync("blueprints", default);

    // Add JSON-LD context if requested
    if (context.AcceptsJsonLd())
    {
        created = JsonLdHelper.EnsureJsonLdContext(created);
    }

    return Results.Created($"/api/blueprints/{created.Id}", created);
})
.WithName("CreateBlueprint")
.WithSummary("Create new blueprint")
.WithDescription("Create a new blueprint with the provided details. Supports JSON-LD via Accept: application/ld+json header.");

// <summary>
// Update existing blueprint
// </summary>
blueprintGroup.MapPut("/{id}", async (HttpContext context, string id, BlueprintModel blueprint, IBlueprintService service, IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var updated = await service.UpdateAsync(id, blueprint, orgId);
    if (updated is null) return Results.NotFound();

    await cache.EvictByTagAsync("blueprints", default);
    return Results.Ok(updated);
})
.WithName("UpdateBlueprint")
.WithSummary("Update blueprint")
.WithDescription("Update an existing blueprint with new details");

// <summary>
// Delete blueprint (soft delete)
// </summary>
blueprintGroup.MapDelete("/{id}", async (HttpContext context, string id, IBlueprintService service, IOutputCacheStore cache) =>
{
    var orgId = context.IsServiceToken() ? null : context.GetOrganizationId();
    var deleted = await service.DeleteAsync(id, orgId);
    if (!deleted) return Results.NotFound();

    await cache.EvictByTagAsync("blueprints", default);
    return Results.NoContent();
})
.WithName("DeleteBlueprint")
.WithSummary("Delete blueprint")
.WithDescription("Soft delete a blueprint (can be recovered)");

// ===========================
// Blueprint Publishing Endpoints
// ===========================

// <summary>
// Validate blueprint (no side effects)
// </summary>
blueprintGroup.MapPost("/{id}/validate", async (string id, IPublishService service) =>
{
    var result = await service.ValidateAsync(id);
    return Results.Ok(result);
})
.WithName("ValidateBlueprint")
.WithSummary("Validate blueprint")
.WithDescription("Validate a blueprint without publishing. Returns validation errors and warnings.")
.RequireAuthorization("CanPublishBlueprints");

// <summary>
// Publish blueprint to a register — Feature 142: governance-hard + rehearsal-soft gated.
// </summary>
blueprintGroup.MapPost("/{id}/publish", async (
    HttpContext httpContext,
    string id,
    IPublishService service,
    Sorcha.Blueprint.Service.Services.Implementation.IPublishGate publishGate,
    Sorcha.Blueprint.Service.Storage.IPublishOverrideStore overrideStore,
    Sorcha.Blueprint.Service.Services.Implementation.BlueprintDesignerMetrics designerMetrics,
    ILogger<Program> publishLogger,
    IOutputCacheStore cache,
    HttpRequest request) =>
{
    // Read required registerId (+ optional override) from JSON body.
    PublishRequest? body = null;
    if (request.ContentLength > 0)
    {
        try
        {
            body = await request.ReadFromJsonAsync<PublishRequest>();
        }
        catch
        {
            return Results.BadRequest(new { error = "Invalid request body. Expected JSON with 'registerId' property." });
        }
    }

    if (body is null || string.IsNullOrWhiteSpace(body.RegisterId))
    {
        return Results.BadRequest(new { error = "registerId is required. Blueprints must be published to a specific register." });
    }

    // Resolve the caller identity from the JWT for the governance check + override attribution.
    var sub = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? httpContext.User.FindFirst("sub")?.Value;
    _ = Guid.TryParse(sub, out var platformUserId);
    var caller = new Sorcha.Blueprint.Service.Services.Implementation.PublishCaller(
        PlatformUserId: platformUserId,
        OrganizationId: httpContext.GetOrganizationId(),
        WalletAddress: httpContext.User.FindFirst("wallet_address")?.Value);

    var overrideConfirmed = body.Override is { Confirm: true };

    // Feature 142 — evaluate the server-side publish gate BEFORE any publish (FR-027 hard, FR-032 soft).
    Sorcha.Blueprint.Service.Services.Implementation.PublishGateDecision decision;
    try
    {
        decision = await publishGate.EvaluateAsync(
            caller, id, body.RegisterId, overrideConfirmed, httpContext.RequestAborted);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = $"Blueprint {id} not found." });
    }

    switch (decision.Outcome)
    {
        case Sorcha.Blueprint.Service.Services.Implementation.PublishGateOutcome.Forbidden:
            // FR-027 — hard refuse; no record written, no publish.
            return Results.Json(
                new { error = decision.Reason ?? "Caller lacks register governance publish rights." },
                statusCode: StatusCodes.Status403Forbidden);

        case Sorcha.Blueprint.Service.Services.Implementation.PublishGateOutcome.RehearsalRequired:
            // FR-032 — soft gate blocked; resend with override to proceed. No publish.
            return Results.Json(
                new { code = "REHEARSAL_REQUIRED", execDefHash = decision.ExecDefHash, message = "This blueprint version has not been rehearsed. Run a full rehearsal, or resend with an override to publish anyway." },
                statusCode: StatusCodes.Status409Conflict);
    }

    var overridden = decision.Outcome
        == Sorcha.Blueprint.Service.Services.Implementation.PublishGateOutcome.ProceedWithOverride;

    var result = await service.PublishAsync(id, body.RegisterId);

    if (!result.IsSuccess)
    {
        return Results.BadRequest(new { errors = result.Errors });
    }

    // FR-032 — record the audited override AFTER a successful publish, using the version actually
    // published, so the audit row references a real immutable record.
    if (overridden)
    {
        var reasonProvided = !string.IsNullOrWhiteSpace(body.Override?.Reason);
        await overrideStore.RecordAsync(new Sorcha.Blueprint.Service.Models.PublishOverride
        {
            BlueprintId = id,
            Version = result.PublishedBlueprint!.Version,
            RegisterId = body.RegisterId,
            ExecDefHash = decision.ExecDefHash,
            OverriddenByPlatformUserId = caller.PlatformUserId,
            OverriddenAt = DateTimeOffset.UtcNow,
            Reason = body.Override?.Reason,
        }, httpContext.RequestAborted);

        // T058 — count the override + emit an operator-visible audit line.
        designerMetrics.RecordPublishOverride(body.RegisterId, reasonProvided);
        publishLogger.LogInformation(
            "Publish override recorded: blueprint={BlueprintId} register={RegisterId} version={Version} actor={ActorSubject} reasonProvided={ReasonProvided}",
            id, body.RegisterId, result.PublishedBlueprint!.Version, caller.PlatformUserId, reasonProvided);
    }

    await cache.EvictByTagAsync("blueprints", default);
    await cache.EvictByTagAsync("published", default);

    // Include warnings (e.g., cycle detection) in the response alongside the gate outcome.
    if (result.Warnings.Length > 0)
    {
        return Results.Ok(new
        {
            blueprintId = result.PublishedBlueprint!.BlueprintId,
            version = result.PublishedBlueprint.Version,
            // Feature 194: the definition just published, by content. `version` is a display label
            // (insert order, re-derived on recovery); this is what an instance is pinned to and what
            // a caller needs if it wants to name this exact definition later.
            execDefHash = result.PublishedBlueprint.ExecDefHash,
            registerId = body.RegisterId,
            publishedAt = result.PublishedBlueprint.PublishedAt,
            overridden,
            warnings = result.Warnings
        });
    }

    return Results.Ok(new
    {
        blueprintId = result.PublishedBlueprint!.BlueprintId,
        version = result.PublishedBlueprint.Version,
        execDefHash = result.PublishedBlueprint.ExecDefHash,
        registerId = body.RegisterId,
        publishedAt = result.PublishedBlueprint.PublishedAt,
        overridden
    });
})
.WithName("PublishBlueprint")
.WithSummary("Publish blueprint (governance-hard + rehearsal-soft gated)")
.WithDescription("Validate and publish a blueprint to a register. Requires { registerId } in the body. "
    + "Enforces register governance rights server-side (403 if the caller lacks Owner/Admin/Designer on the register). "
    + "Then checks the rehearsal soft gate: the publishing version's executable-definition hash must match a recorded "
    + "rehearsal pass, otherwise 409 REHEARSAL_REQUIRED unless { override: { confirm: true, reason? } } is sent, which "
    + "publishes and records an audited override. The 200 response carries 'overridden'.")
.RequireAuthorization("CanPublishBlueprints");

// <summary>
// Get all published versions of a blueprint
// </summary>
blueprintGroup.MapGet("/{id}/versions", async (string id, IPublishedBlueprintStore store) =>
{
    var versions = await store.GetVersionsAsync(id);
    return Results.Ok(versions);
})
.WithName("GetBlueprintVersions")
.WithSummary("Get blueprint versions")
.WithDescription("Retrieve all published versions of a blueprint")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("published"));

// <summary>
// Get specific published version
// </summary>
blueprintGroup.MapGet("/{id}/versions/{version}", async (string id, int version, IPublishedBlueprintStore store) =>
{
    var published = await store.GetVersionAsync(id, version);
    return published is not null ? Results.Ok(published) : Results.NotFound();
})
.WithName("GetBlueprintVersion")
.WithSummary("Get specific version")
.WithDescription("Retrieve a specific published version of a blueprint (immutable)")
.CacheOutput(policy => policy.Expire(TimeSpan.FromDays(365)).Tag("published")); // Cache permanently - immutable

// <summary>
// Feature 194 — get one published DEFINITION by its executable-definition hash (the pin).
// </summary>
// <remarks>
// This is how a running instance's definition is resolved. The by-ordinal sibling above cannot
// serve that purpose: the ordinal is assigned from insert order and re-derived on recovery, so it
// does not reliably denote the same definition twice. The response body is the blueprint itself
// (not the PublishedBlueprint envelope) because the validator deserialises it straight into its
// blueprint model.
//
// Content-addressed, therefore immutable — cached permanently, like the by-ordinal endpoint.
// </remarks>
blueprintGroup.MapGet("/{id}/definitions/{publicationTxId}", async (
    string id, string publicationTxId, IPublishedBlueprintStore store) =>
{
    var published = await store.GetByPublicationAsync(id, publicationTxId);
    return published is not null ? Results.Ok(published.Blueprint) : Results.NotFound();
})
.WithName("GetBlueprintDefinition")
.WithSummary("Get a pinned blueprint definition")
.WithDescription(
    "Retrieve the exact published definition identified by the transaction that published it — the "
    + "definition a running instance is pinned to. Returns 404 when this node cannot resolve it; "
    + "callers MUST treat that as a refusal and never fall back to the latest definition.")
.CacheOutput(policy => policy.Expire(TimeSpan.FromDays(365)).Tag("published"));

// ===========================
// Rehearsal Endpoints (Feature 142 — Blueprint Design Lifecycle, US2)
// ===========================

// Map full-rehearsal walk-through endpoints (POST/GET/DELETE under /api/blueprints/{id}/rehearsals).
app.MapRehearsalEndpoints();

// Feature 142 (T056 / US6) — amend / clone-to-draft endpoint (POST /api/blueprints/from-published).
app.MapBlueprintFromPublishedEndpoint();

// ===========================
// Schema Endpoints (Sprint 7 - Schema Store)
// ===========================

// Map schema store endpoints (GET /api/v1/schemas/system, GET /api/v1/schemas/{identifier}, etc.)
app.MapSchemaEndpoints();

// Map schema library endpoints (034-schema-library)
app.MapSchemaLibraryEndpoints();

// Map credential lifecycle endpoints (POST /api/v1/credentials/{credentialId}/revoke)
app.MapCredentialEndpoints();

// Map status list endpoints (GET public, POST/PUT internal)
app.MapStatusListEndpoints();

// Map pending action endpoints (Feature 062)
app.MapActionEndpoints();
// Feature 176 — disclosed prior-action data query (agent + MCP participant tools consume this).
app.MapWorkflowDisclosureEndpoints();

// Map file chunk submission endpoints (Feature 085 — Stored Data Transactions)
app.MapFileChunkEndpoints();

// Feature 154 (B) — citizen service catalogue (consumer-tier: list startable services).
app.MapCatalogueEndpoints();

// Feature 111 — Timebound Presentation Lifecycle endpoints.
var presentationGroup = app.MapGroup("/api/presentations")
    .WithTags("Presentations")
    .RequireAuthorization();
presentationGroup.MapPresentationEndpoints();

// ===========================
// Template Endpoints
// ===========================

var templateGroup = app.MapGroup("/api/templates")
    .WithTags("Templates")
    .RequireAuthorization();

// <summary>
// Get all published templates
// </summary>
templateGroup.MapGet("/", async (Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service, string? category = null) =>
{
    var templates = category != null
        ? await service.GetTemplatesByCategoryAsync(category)
        : await service.GetPublishedTemplatesAsync();

    return Results.Ok(templates);
})
.WithName("GetTemplates")
.WithSummary("Get all published templates")
.WithDescription("Retrieve all published blueprint templates, optionally filtered by category")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("templates"));

// <summary>
// Get template by ID
// </summary>
templateGroup.MapGet("/{id}", async (string id, Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var template = await service.GetTemplateAsync(id);
    return template is not null ? Results.Ok(template) : Results.NotFound();
})
.WithName("GetTemplateById")
.WithSummary("Get template by ID")
.WithDescription("Retrieve a specific blueprint template by its unique identifier")
.CacheOutput(policy => policy.Expire(TimeSpan.FromMinutes(10)).Tag("templates"));

// <summary>
// Create or update a template
// </summary>
templateGroup.MapPost("/", async (
    Sorcha.Blueprint.Models.BlueprintTemplate template,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    var saved = await service.SaveTemplateAsync(template);
    await cache.EvictByTagAsync("templates", default);

    return Results.Ok(saved);
})
.WithName("SaveTemplate")
.WithSummary("Create or update template")
.WithDescription("Create a new template or update an existing one");

// <summary>
// Delete a template
// </summary>
templateGroup.MapDelete("/{id}", async (
    string id,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    var deleted = await service.DeleteTemplateAsync(id);
    if (!deleted) return Results.NotFound();

    await cache.EvictByTagAsync("templates", default);
    return Results.NoContent();
})
.WithName("DeleteTemplate")
.WithSummary("Delete template")
.WithDescription("Delete a blueprint template");

// <summary>
// Evaluate a template with parameters to generate a blueprint
// </summary>
templateGroup.MapPost("/evaluate", async (
    Sorcha.Blueprint.Models.TemplateEvaluationRequest request,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.EvaluateTemplateAsync(request);

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
})
.WithName("EvaluateTemplate")
.WithSummary("Evaluate template")
.WithDescription("Evaluate a blueprint template with specific parameters to generate a blueprint");

// <summary>
// Validate template parameters
// </summary>
templateGroup.MapPost("/{id}/validate", async (
    string id,
    Dictionary<string, object> parameters,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.ValidateParametersAsync(id, parameters);

    return Results.Ok(new
    {
        valid = result.IsValid,
        errors = result.Errors,
        warnings = result.Warnings
    });
})
.WithName("ValidateTemplateParameters")
.WithSummary("Validate parameters")
.WithDescription("Validate parameters against a template's parameter schema");

// <summary>
// Evaluate a template example
// </summary>
templateGroup.MapGet("/{id}/examples/{exampleName}", async (
    string id,
    string exampleName,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service) =>
{
    var result = await service.EvaluateExampleAsync(id, exampleName);

    if (!result.Success)
    {
        return Results.BadRequest(result);
    }

    return Results.Ok(result);
})
.WithName("EvaluateTemplateExample")
.WithSummary("Evaluate template example")
.WithDescription("Evaluate a predefined example from the template");

templateGroup.MapPost("/{id}/increment-usage", async (
    string id,
    Sorcha.Blueprint.Service.Templates.IBlueprintTemplateService service,
    IOutputCacheStore cache) =>
{
    await service.IncrementUsageAsync(id);
    await cache.EvictByTagAsync("templates", default);
    return Results.Ok();
})
.WithName("IncrementTemplateUsage")
.WithSummary("Increment template usage count")
.WithDescription("Increments the usage counter for a template after it has been used to create a blueprint");

// ===========================
// Action API Endpoints (Sprint 4)
// ===========================

var actionsGroup = app.MapGroup("/api/actions")
    .WithTags("Actions")
    .RequireAuthorization("CanExecuteBlueprints");

// <summary>
// Get available blueprints for a wallet/register combination
// </summary>
actionsGroup.MapGet("/{wallet}/{register}/blueprints", async (
    string wallet,
    string register,
    HttpContext httpContext,
    IPublishedBlueprintStore publishedStore,
    Sorcha.ServiceClients.Wallet.IWalletServiceClient walletClient,
    CancellationToken ct) =>
{
    // Authorize: the caller must own the {wallet} they are querying as. CanExecuteBlueprints is only
    // "any authenticated user", so without this any authenticated caller could pass any wallet in the
    // path. Resolve the caller's wallets the same way the rest of the service does — the wallet_address
    // claim, else a Wallet-Service owner lookup, since consumer-tier tokens omit the claim (Feature 136);
    // a gate reading the claim alone would 403 every real citizen. Fail closed on an empty resolved set.
    var callerWallets = await Sorcha.Blueprint.Service.Services.Infrastructure.ParticipantWalletResolver
        .ResolveUserWalletAddressesAsync(httpContext, walletClient, logger, ct);
    if (!callerWallets.Any(w => string.Equals(w, wallet, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Problem(
            "You do not own the wallet in this request.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    // Get only blueprints published to this specific register
    var publishedForRegister = await publishedStore.GetByRegisterAsync(register);
    var availableBlueprints = publishedForRegister.Select(pub =>
    {
        var availableActions = pub.Blueprint.Actions
            .Select(a => new Sorcha.Blueprint.Service.Models.Responses.ActionInfo
            {
                ActionId = a.Id.ToString(),
                Title = a.Title,
                Description = a.Description,
                IsAvailable = true, // TODO: Apply routing rules
                DataSchema = a.DataSchemas?.FirstOrDefault() is { } schema
                    && schema.RootElement.TryGetProperty("$id", out var schemaId)
                    ? schemaId.GetString() : null
            })
            .ToList();

        return new Sorcha.Blueprint.Service.Models.Responses.BlueprintInfo
        {
            BlueprintId = pub.BlueprintId,
            Title = pub.Blueprint.Title,
            Description = pub.Blueprint.Description,
            Version = pub.Version,
            AvailableActions = availableActions
        };
    }).ToList();

    var response = new Sorcha.Blueprint.Service.Models.Responses.AvailableBlueprintsResponse
    {
        WalletAddress = wallet,
        RegisterAddress = register,
        Blueprints = availableBlueprints
    };

    return Results.Ok(response);
})
.WithName("GetAvailableBlueprints")
.WithSummary("Get available blueprints")
.WithDescription("Retrieve blueprints and actions available to a specific wallet/register combination. "
    + "The caller must own the wallet in the path (403 otherwise).")
.Produces(StatusCodes.Status403Forbidden);
// No .CacheOutput here: the endpoint is now caller-specific, and the previous route-only cache policy
// (keyed on {wallet}/{register} with no VaryBy on caller identity) could serve one caller's authorized
// result to another. It was also inert under an auth-required group, so nothing is lost by dropping it.

// Get a single published blueprint (with full action schemas) for a wallet /
// register / blueprintId. Returns the latest version published to this
// specific register. Used by the New Submissions workspace to render forms
// from published blueprints on any node — NOT from the draft store. Fixes
// the 404 bug where non-authoring nodes could not start submissions.
// (.WithSummary / .WithDescription on the endpoint provide the OpenAPI doc.)
actionsGroup.MapGet("/{wallet}/{register}/blueprints/{blueprintId}", async (
    string wallet,
    string register,
    string blueprintId,
    HttpContext httpContext,
    IPublishedBlueprintStore publishedStore,
    Sorcha.ServiceClients.Wallet.IWalletServiceClient walletClient,
    CancellationToken ct) =>
{
    // Same ownership gate as the list endpoint above — the caller must own {wallet}.
    var callerWallets = await Sorcha.Blueprint.Service.Services.Infrastructure.ParticipantWalletResolver
        .ResolveUserWalletAddressesAsync(httpContext, walletClient, logger, ct);
    if (!callerWallets.Any(w => string.Equals(w, wallet, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.Problem(
            "You do not own the wallet in this request.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var publishedForRegister = await publishedStore.GetByRegisterAsync(register);
    var match = publishedForRegister
        .Where(pub => string.Equals(pub.BlueprintId, blueprintId, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(pub => pub.Version)
        .FirstOrDefault();

    if (match is null)
    {
        return Results.NotFound(new { error = $"Blueprint '{blueprintId}' is not published to register '{register}'." });
    }

    return Results.Ok(match.Blueprint);
})
.WithName("GetPublishedBlueprintDetail")
.WithSummary("Get published blueprint detail")
.WithDescription("Retrieve a single published blueprint including full action schemas. Sourced from the published blueprint store — works on any node, not just the authoring node. The caller must own the wallet in the path (403 otherwise).")
.Produces(StatusCodes.Status403Forbidden);
// No .CacheOutput — see the note on the sibling list endpoint above.

// <summary>
// Get actions for a wallet/register (paginated)
// </summary>
actionsGroup.MapGet("/{wallet}/{register}", async (
    string wallet,
    string register,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    int page = 1,
    int pageSize = 20) =>
{
    var skip = (page - 1) * pageSize;
    var actions = await actionStore.GetActionsAsync(wallet, register, skip, pageSize);
    var totalCount = await actionStore.GetActionCountAsync(wallet, register);

    var result = new PagedResult<Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse>
    {
        Items = actions,
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    };

    return Results.Ok(result);
})
.WithName("GetActions")
.WithSummary("Get actions for wallet/register")
.WithDescription("Retrieve paginated list of actions for a specific wallet and register");

// <summary>
// Get a specific action by transaction hash
// </summary>
actionsGroup.MapGet("/{wallet}/{register}/{tx}", async (
    string wallet,
    string register,
    string tx,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore) =>
{
    var action = await actionStore.GetActionAsync(tx);

    if (action == null)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    // Verify the action belongs to this wallet/register
    if (action.SenderWallet != wallet || action.RegisterAddress != register)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    return Results.Ok(action);
})
.WithName("GetActionDetails")
.WithSummary("Get action details")
.WithDescription("Retrieve detailed information about a specific action transaction");

// <summary>
// Submit a new action
// </summary>
actionsGroup.MapPost("/", async (
    Sorcha.Blueprint.Service.Models.Requests.ActionSubmissionRequest request,
    HttpContext context,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionResolverService actionResolver,
    Sorcha.Blueprint.Service.Services.Interfaces.IPayloadResolverService payloadResolver,
    Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService txBuilder,
    Sorcha.ServiceClients.Wallet.IWalletServiceClient walletClient,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore,
    Sorcha.Cryptography.Interfaces.IHashProvider hashProvider,
    Sorcha.Blueprint.Engine.Interfaces.IDisclosureProcessor disclosureProcessor,
    Sorcha.Blueprint.Service.Endpoints.FileUploadSessionStore fileSessionStore) =>
{
    try
    {
        // 0a. Inject file upload master keys into payload before encryption.
        // File references contain an uploadSessionId pointing to the server-side session
        // that holds the master encryption key. We inject the key (base64) into the payload
        // so it gets encrypted per-recipient by the encryption pipeline.
        if (request.PayloadData != null)
        {
            logger.LogInformation("[085] PayloadData has {Count} fields. Types: {Types}",
                request.PayloadData.Count,
                string.Join(", ", request.PayloadData.Select(kv => $"{kv.Key}:{kv.Value?.GetType().Name ?? "null"}")));
            foreach (var kvp in request.PayloadData.ToList())
            {
                if (kvp.Value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    // Check if this is a file reference with chunkTransactionIds
                    var hasChunks = el.TryGetProperty("chunkTransactionIds", out _);
                    var hasMkId = el.TryGetProperty("masterKeyId", out var mkId);
                    var mkIdVal = hasMkId ? mkId.GetString() : "N/A";
                    logger.LogInformation("[085] Field '{Field}': hasChunks={HasChunks}, masterKeyId={MkId}", kvp.Key, hasChunks, mkIdVal);

                    if (hasChunks && hasMkId && mkIdVal == "server-managed")
                    {
                        string? sessionId = null;
                        if (el.TryGetProperty("uploadSessionId", out var sid))
                            sessionId = sid.GetString();
                        logger.LogInformation("[085] Field '{Field}': uploadSessionId={SessionId}", kvp.Key, sessionId ?? "NULL");

                        if (sessionId != null && fileSessionStore.TryGetSession(sessionId, out var masterKey, out _))
                        {
                            // Inject masterKeyBase64 into the raw JSON string, then re-parse.
                            // This avoids Dictionary<string,object> serialization issues with JsonElement values.
                            var rawJson = el.GetRawText();
                            var keyB64 = Convert.ToBase64String(masterKey);
                            // Append masterKeyBase64, update masterKeyId, remove uploadSessionId
                            var injected = rawJson.TrimEnd('}') +
                                ",\"masterKeyBase64\":\"" + keyB64 + "\"}";
                            injected = injected.Replace("\"server-managed\"", "\"embedded\"");
                            injected = System.Text.RegularExpressions.Regex.Replace(
                                injected, ",?\"uploadSessionId\":\"[^\"]*\"", "");
                            using var doc = System.Text.Json.JsonDocument.Parse(injected);
                            request.PayloadData[kvp.Key] = doc.RootElement.Clone();
                        }
                    }
                }
            }
        }

        // 0b. Replay protection — check idempotency key
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            // Auto-generate from request content to prevent duplicate submissions
            var keySource = $"{request.BlueprintId}:{request.ActionId}:{request.InstanceId}:{request.SenderWallet}:{request.RegisterAddress}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var keyHash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(keySource));
            idempotencyKey = BitConverter.ToString(keyHash).Replace("-", "").ToLowerInvariant();
        }

        var existingTxHash = await actionStore.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existingTxHash != null)
        {
            return Results.Conflict(new { error = "Duplicate submission", transactionHash = existingTxHash });
        }

        // 1. Get the definition THIS INSTANCE is pinned to (Feature 195).
        //
        // Resolving by blueprint id alone would validate the submission against whatever definition
        // this node happens to hold latest — which is the defect version pinning exists to remove,
        // and it fails silently: the payload is checked against rules the instance never agreed to
        // run, and the routing decision is then labelled with the instance's actual pin.
        var pinnedInstance = await instanceStore.GetAsync(request.InstanceId);
        if (pinnedInstance == null)
        {
            return Results.BadRequest(new { error = $"Instance {request.InstanceId} not found" });
        }

        var blueprint = await actionResolver.GetBlueprintAsync(
            request.BlueprintId, pinnedInstance.BlueprintDefinitionTxId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // 2. Get action definition
        var actionDef = actionResolver.GetActionDefinition(blueprint, request.ActionId);
        if (actionDef == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // 3. Process disclosure rules to determine which data each participant receives
        var disclosureResults = new Dictionary<string, object>();
        var participantWalletMap = new Dictionary<string, string>();

        // Disclosure processing and the sender's full-payload entry both require a non-null map;
        // a null payload is treated as an empty payload.
        var payloadData = request.PayloadData ?? new Dictionary<string, object>();

        var actionDisclosures = actionDef.Disclosures?.ToList();
        if (actionDisclosures != null && actionDisclosures.Count > 0)
        {
            // Apply disclosure rules: each participant gets only their authorized fields
            var engineDisclosures = disclosureProcessor.CreateDisclosures(
                payloadData,
                actionDisclosures);

            foreach (var disclosure in engineDisclosures)
            {
                // Resolve participant ID to wallet address from blueprint participants
                var participant = blueprint.Participants.FirstOrDefault(p => p.Id == disclosure.ParticipantId);
                var walletAddress = participant?.WalletAddress ?? disclosure.ParticipantId;

                disclosureResults[walletAddress] = disclosure.DisclosedData;
                participantWalletMap[disclosure.ParticipantId] = walletAddress;
            }
        }

        // Ensure sender always receives the full payload
        if (!disclosureResults.ContainsKey(request.SenderWallet))
        {
            disclosureResults[request.SenderWallet] = payloadData;
            participantWalletMap[request.SenderWallet] = request.SenderWallet;
        }

        // 4. Create encrypted payloads using Wallet Service
        var encryptedPayloads = await payloadResolver.CreateEncryptedPayloadsAsync(
            disclosureResults,
            participantWalletMap,
            request.SenderWallet);

        // 5. Compute PrevTxId server-side instead of trusting client.
        // For Action 0 (no prior TX): PrevTxId = blueprint's publish TX on this register.
        // For subsequent actions: use client-provided PrevTxId (from prior step).
        var previousTxId = request.PreviousTransactionHash;
        if (string.IsNullOrEmpty(previousTxId))
        {
            // Action 0 chains from the transaction that PUBLISHED the definition this instance
            // runs (Feature 195). Read from the instance's pin, never recomputed: anchor and pin are
            // one value because they are one fact, and the formula this replaced had four homes.
            previousTxId = pinnedInstance.BlueprintDefinitionTxId;
            logger.LogInformation(
                "Action 0 for blueprint {BlueprintId}: PrevTxId set to its definition's publication {TxId}",
                request.BlueprintId, previousTxId);
        }

        var transaction = await txBuilder.BuildActionTransactionAsync(
            request.BlueprintId,
            request.ActionId,
            request.InstanceId,
            previousTxId,
            encryptedPayloads,
            request.SenderWallet,
            request.RegisterAddress);

        // 6. Calculate transaction hash
        var txHashBytes = System.Text.Encoding.UTF8.GetBytes(transaction.TxId ?? Guid.NewGuid().ToString());
        using var txHashStream = new System.IO.MemoryStream(txHashBytes);
        var txHash = await hashProvider.ComputeHashAsync(txHashStream);
        var txHashHex = BitConverter.ToString(txHash).Replace("-", "").ToLowerInvariant();

        // 7. Sign the transaction with Wallet Service
        var transactionBytes = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(transaction));
        var signResult = await walletClient.SignTransactionAsync(
            request.SenderWallet,
            transactionBytes,
            derivationPath: null); // Use wallet's default signing key

        // 7b. Verify the signature we just created
        var walletInfo = await walletClient.GetWalletAsync(request.SenderWallet);
        if (walletInfo == null)
        {
            return Results.Problem("Sender wallet not found", statusCode: 400);
        }

        var isSignatureValid = await walletClient.VerifySignatureAsync(
            walletInfo.PublicKey,
            Base64Url.EncodeToString(transactionBytes),
            Base64Url.EncodeToString(signResult.Signature),
            walletInfo.Algorithm);

        if (!isSignatureValid)
        {
            logger.LogError("Signature verification failed for wallet {Wallet}", request.SenderWallet);
            return Results.Problem("Transaction signature verification failed", statusCode: 400);
        }

        // 8. Convert to Register TransactionModel and submit to Register Service
        var registerTransaction = new Sorcha.Register.Models.TransactionModel
        {
            TxId = txHashHex,
            RegisterId = request.RegisterAddress,
            SenderWallet = request.SenderWallet,
            // RecipientsWallets parity with the action-executor path so the Register
            // Service's InboundTransactionRouter can notify recipients on seal. The
            // encryptedPayloads dictionary is keyed by recipient wallet address, so
            // its keys ARE the recipients list. The action-executor path populates
            // this via BuiltTransaction.RecipientsWallets; this legacy /api/actions
            // POST entry point was dropping it.
            RecipientsWallets = encryptedPayloads.Keys.ToList(),
            TimeStamp = DateTime.UtcNow,
            PrevTxId = previousTxId ?? string.Empty,
            MetaData = transaction.Metadata != null ?
                System.Text.Json.JsonSerializer.Deserialize<Sorcha.Register.Models.TransactionMetaData>(transaction.Metadata) : null,
            Payloads = encryptedPayloads.Select(kvp => new Sorcha.Register.Models.PayloadModel
            {
                Data = Base64Url.EncodeToString(kvp.Value),
                WalletAccess = new[] { kvp.Key },
                ContentEncoding = "base64url"
            }).ToArray(),
            PayloadCount = (ulong)encryptedPayloads.Count,
            Signature = Base64Url.EncodeToString(signResult.Signature)
        };

        // Submit to Register Service
        await registerClient.SubmitTransactionAsync(request.RegisterAddress, registerTransaction);

        // 9. Build file transactions if any
        List<string>? fileHashes = null;
        if (request.Files != null && request.Files.Any())
        {
            var fileAttachments = request.Files.Select(f => new Sorcha.Blueprint.Service.Services.Interfaces.FileAttachment(
                f.FileName,
                f.ContentType,
                Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(f.ContentBase64)
            )).ToList();

            var fileTxs = await txBuilder.BuildFileTransactionsAsync(
                fileAttachments,
                txHashHex,
                request.SenderWallet,
                request.RegisterAddress);

            fileHashes = new List<string>();
            var fileMetadataList = new List<Sorcha.Blueprint.Service.Models.Responses.FileMetadata>();

            for (int i = 0; i < fileTxs.Count; i++)
            {
                var fileTx = fileTxs[i];
                var fileHashBytes = System.Text.Encoding.UTF8.GetBytes(fileTx.TxId ?? Guid.NewGuid().ToString());
                using var fileHashStream = new System.IO.MemoryStream(fileHashBytes);
                var fileHash = await hashProvider.ComputeHashAsync(fileHashStream);
                var fileHashHex = BitConverter.ToString(fileHash).Replace("-", "").ToLowerInvariant();
                fileHashes.Add(fileHashHex);

                // Store file content and metadata
                var fileAttachment = fileAttachments[i];
                await actionStore.StoreFileContentAsync(fileHashHex, fileAttachment.Content);

                var fileMeta = new Sorcha.Blueprint.Service.Models.Responses.FileMetadata
                {
                    FileId = fileHashHex,
                    FileName = fileAttachment.FileName,
                    ContentType = fileAttachment.ContentType,
                    Size = fileAttachment.Content.Length
                };

                await actionStore.StoreFileMetadataAsync(txHashHex, fileHashHex, fileMeta);
                fileMetadataList.Add(fileMeta);
            }
        }

        // 10. Generate instance ID if needed
        var instanceId = request.InstanceId ?? Guid.NewGuid().ToString();

        // 11. Store action locally
        var actionDetails = new Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse
        {
            TransactionHash = txHashHex,
            BlueprintId = request.BlueprintId,
            ActionId = request.ActionId,
            InstanceId = instanceId,
            SenderWallet = request.SenderWallet,
            RegisterAddress = request.RegisterAddress,
            PayloadData = request.PayloadData,
            Timestamp = DateTimeOffset.UtcNow,
            PreviousTransactionHash = request.PreviousTransactionHash
        };

        await actionStore.StoreActionAsync(actionDetails);

        // Store idempotency key (24-hour TTL)
        await actionStore.StoreIdempotencyKeyAsync(idempotencyKey, txHashHex, TimeSpan.FromHours(24));

        // 12. Return response
        var response = new Sorcha.Blueprint.Service.Models.Responses.ActionSubmissionResponse
        {
            TransactionId = txHashHex,
            InstanceId = instanceId,
            SerializedTransaction = System.Text.Json.JsonSerializer.Serialize(transaction),
            FileTransactionHashes = fileHashes,
            Timestamp = DateTimeOffset.UtcNow
        };

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithRequestValidation()
.WithName("SubmitAction")
.WithSummary("Submit an action")
.WithDescription("Submit a new action for execution in a blueprint workflow");

// <summary>
// Reject a pending action
// </summary>
actionsGroup.MapPost("/reject", async (
    Sorcha.Blueprint.Service.Models.Requests.ActionRejectionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.ITransactionBuilderService txBuilder,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore,
    Sorcha.Cryptography.Interfaces.IHashProvider hashProvider) =>
{
    try
    {
        // Validate required fields
        if (string.IsNullOrEmpty(request.TransactionHash))
        {
            return Results.BadRequest(new { error = "TransactionHash is required" });
        }
        if (string.IsNullOrEmpty(request.SenderWallet))
        {
            return Results.BadRequest(new { error = "SenderWallet is required" });
        }
        if (string.IsNullOrEmpty(request.RegisterAddress))
        {
            return Results.BadRequest(new { error = "RegisterAddress is required" });
        }

        // 1. Verify original transaction exists
        var originalAction = await actionStore.GetActionAsync(request.TransactionHash);
        if (originalAction == null)
        {
            return Results.NotFound(new { error = "Original transaction not found" });
        }

        // 2. Build rejection transaction
        var rejectionTx = await txBuilder.BuildRejectionTransactionAsync(
            request.TransactionHash,
            request.Reason,
            request.SenderWallet,
            request.RegisterAddress);

        // 3. Calculate rejection transaction hash
        var rejectionHashBytes = System.Text.Encoding.UTF8.GetBytes(rejectionTx.TxId ?? Guid.NewGuid().ToString());
        using var rejectionHashStream = new System.IO.MemoryStream(rejectionHashBytes);
        var rejectionHash = await hashProvider.ComputeHashAsync(rejectionHashStream);
        var rejectionHashHex = BitConverter.ToString(rejectionHash).Replace("-", "").ToLowerInvariant();

        // 4. Convert to Register TransactionModel and submit to Register Service
        var registerRejection = new Sorcha.Register.Models.TransactionModel
        {
            TxId = rejectionHashHex,
            RegisterId = request.RegisterAddress,
            SenderWallet = request.SenderWallet,
            TimeStamp = DateTime.UtcNow,
            PrevTxId = request.TransactionHash,
            MetaData = rejectionTx.Metadata != null ?
                System.Text.Json.JsonSerializer.Deserialize<Sorcha.Register.Models.TransactionMetaData>(rejectionTx.Metadata) : null,
            Payloads = Array.Empty<Sorcha.Register.Models.PayloadModel>()
        };

        // Submit rejection to Register Service
        await registerClient.SubmitTransactionAsync(request.RegisterAddress, registerRejection);

        // 5. Store rejection action locally
        var rejectionDetails = new Sorcha.Blueprint.Service.Models.Responses.ActionDetailsResponse
        {
            TransactionHash = rejectionHashHex,
            BlueprintId = originalAction.BlueprintId,
            ActionId = "rejection",
            InstanceId = originalAction.InstanceId,
            SenderWallet = request.SenderWallet,
            RegisterAddress = request.RegisterAddress,
            PayloadData = new Dictionary<string, object>
            {
                ["rejectedTransactionHash"] = request.TransactionHash,
                ["reason"] = request.Reason
            },
            Timestamp = DateTimeOffset.UtcNow,
            PreviousTransactionHash = request.TransactionHash
        };

        await actionStore.StoreActionAsync(rejectionDetails);

        // 6. Return response
        var response = new Sorcha.Blueprint.Service.Models.Responses.ActionSubmissionResponse
        {
            TransactionId = rejectionHashHex,
            InstanceId = rejectionDetails.InstanceId,
            SerializedTransaction = System.Text.Json.JsonSerializer.Serialize(rejectionTx),
            Timestamp = DateTimeOffset.UtcNow
        };

        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("RejectAction")
.WithSummary("Reject an action")
.WithDescription("Reject a pending action with a reason");

// <summary>
// Get file content by file ID
// </summary>
app.MapGet("/api/files/{wallet}/{register}/{tx}/{fileId}", async (
    string wallet,
    string register,
    string tx,
    string fileId,
    Sorcha.Blueprint.Service.Storage.IActionStore actionStore) =>
{
    // 1. Verify action exists and belongs to wallet/register
    var action = await actionStore.GetActionAsync(tx);
    if (action == null || action.SenderWallet != wallet || action.RegisterAddress != register)
    {
        return Results.NotFound(new { error = "Action not found" });
    }

    // 2. Get file metadata
    var metadata = await actionStore.GetFileMetadataAsync(tx, fileId);
    if (metadata == null)
    {
        return Results.NotFound(new { error = "File not found" });
    }

    // 3. Get file content
    var content = await actionStore.GetFileContentAsync(fileId);
    if (content == null)
    {
        return Results.NotFound(new { error = "File content not found" });
    }

    // 4. Return file
    return Results.File(content, metadata.ContentType, metadata.FileName);
})
.WithName("GetFile")
.WithSummary("Get file attachment")
.WithDescription("Retrieve a file attachment from an action transaction")
.WithTags("Actions")
.RequireAuthorization("CanExecuteBlueprints");

// ===========================
// Execution Helper Endpoints (Sprint 5)
// ===========================

var executionGroup = app.MapGroup("/api/execution")
    .WithTags("Execution")
    .RequireAuthorization("CanExecuteBlueprints");

// <summary>
// Validate action data against schema (helper endpoint)
// </summary>
executionGroup.MapPost("/validate", async (
    ValidateRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Validate
        var result = await executionEngine.ValidateAsync(request.Data, action);

        return Results.Ok(new
        {
            isValid = result.IsValid,
            errors = result.Errors.Select(e => new
            {
                path = e.InstanceLocation,
                message = e.Message,
                schemaLocation = e.SchemaLocation,
                keyword = e.Keyword
            })
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("ValidateAction")
.WithSummary("Validate action data")
.WithDescription("Validate action data against the action's JSON Schema without executing the full workflow");

// <summary>
// Apply calculations to action data (helper endpoint)
// </summary>
executionGroup.MapPost("/calculate", async (
    CalculateRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Apply calculations
        var result = await executionEngine.ApplyCalculationsAsync(request.Data, action);

        return Results.Ok(new
        {
            processedData = result,
            calculatedFields = result.Keys.Except(request.Data.Keys).ToList()
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("CalculateAction")
.WithSummary("Apply calculations")
.WithDescription("Apply JSON Logic calculations to action data without executing the full workflow");

// <summary>
// Determine routing for action (helper endpoint)
// </summary>
executionGroup.MapPost("/route", async (
    RouteRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Determine routing
        var result = await executionEngine.DetermineRoutingAsync(blueprint, action, request.Data);

        return Results.Ok(new
        {
            nextActionId = result.NextActionId,
            nextParticipantId = result.NextParticipantId,
            isWorkflowComplete = result.IsWorkflowComplete,
            rejectedToParticipantId = result.RejectedToParticipantId,
            matchedCondition = result.MatchedCondition
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("DetermineRouting")
.WithSummary("Determine routing")
.WithDescription("Determine the next action and participant based on routing conditions");

// <summary>
// Apply disclosure rules (helper endpoint)
// </summary>
executionGroup.MapPost("/disclose", async (
    DiscloseRequest request,
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Engine.Interfaces.IExecutionEngine executionEngine) =>
{
    try
    {
        // Get blueprint
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        if (blueprint == null)
        {
            return Results.BadRequest(new { error = "Blueprint not found" });
        }

        // Get action (parse ActionId string to int)
        if (!int.TryParse(request.ActionId, out var actionIdInt))
        {
            return Results.BadRequest(new { error = "Invalid action ID format" });
        }

        var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionIdInt);
        if (action == null)
        {
            return Results.BadRequest(new { error = "Action not found in blueprint" });
        }

        // Apply disclosures
        var result = executionEngine.ApplyDisclosures(request.Data, action);

        return Results.Ok(new
        {
            disclosures = result.Select(d => new
            {
                participantId = d.ParticipantId,
                disclosedData = d.DisclosedData,
                disclosureId = d.DisclosureId,
                fieldCount = d.DisclosedData.Count
            })
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("ApplyDisclosure")
.WithSummary("Apply disclosure rules")
.WithDescription("Apply selective disclosure rules to see what data each participant will receive");

// ===========================
// Notification Endpoint (Sprint 5)
// ===========================

var notificationGroup = app.MapGroup("/api/notifications")
    .WithTags("Notifications")
    .RequireAuthorization("RequireService");

// <summary>
// Internal endpoint for Register Service to notify of transaction confirmations
// </summary>
notificationGroup.MapPost("/transaction-confirmed", async (
    TransactionConfirmationNotification notification,
    Sorcha.Blueprint.Service.Services.Interfaces.INotificationService notificationService) =>
{
    try
    {
        // Send thin signal via SignalR — transaction confirmed
        if (!string.IsNullOrEmpty(notification.InstanceId) && !string.IsNullOrEmpty(notification.WalletAddress))
        {
            await notificationService.NotifyActionAvailableAsync(
                notification.InstanceId, notification.WalletAddress);
        }

        return Results.Accepted();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("NotifyTransactionConfirmed")
.WithSummary("Notify transaction confirmed")
.WithDescription("Internal endpoint for Register Service to notify of transaction confirmations (requires service authentication)");

// ===========================
// Instance-Based Orchestration Endpoints (Sprint 6)
// ===========================

var instancesGroup = app.MapGroup("/api/instances")
    .WithTags("Instances")
    .RequireAuthorization("CanExecuteBlueprints");

// Feature 145 US4 — internal instance-rebuild surface (service-to-service / operator tooling).
// The materialized instance row is a cache of the ledger projection; these endpoints let an operator
// verify it against a fresh replay (parity self-check) and repair a corrupt/missing view by rebuilding
// from the sealed transactions. Not a public mutation — gated by the service audience.
var instanceRebuildGroup = app.MapGroup("/api/internal/instances")
    .WithTags("Instances")
    .RequireAuthorization("RequireService");

instanceRebuildGroup.MapGet("/{registerId}/{instanceId}/parity", async (
    string registerId,
    string instanceId,
    Sorcha.Blueprint.Service.Services.Implementation.IInstanceRebuildService rebuildService,
    CancellationToken ct) =>
{
    var result = await rebuildService.CheckParityAsync(registerId, instanceId, ct);
    return Results.Ok(new
    {
        instanceId,
        registerId,
        inSync = result.InSync,
        detail = result.Detail,
        rebuiltState = result.Rebuilt?.State.ToString(),
        materializedState = result.Materialized?.State.ToString(),
    });
})
    .WithName("CheckInstanceParity")
    .WithSummary("Check instance projection parity")
    .WithDescription("Rebuilds the instance from the register's sealed transactions and reports whether it matches the materialized view (Feature 145 US4 self-check).");

instanceRebuildGroup.MapPost("/{registerId}/{instanceId}/rebuild", async (
    string registerId,
    string instanceId,
    Sorcha.Blueprint.Service.Services.Implementation.IInstanceRebuildService rebuildService,
    CancellationToken ct) =>
{
    var rebuilt = await rebuildService.RebuildAndPersistAsync(registerId, instanceId, ct);
    return rebuilt is null
        ? Results.NotFound(new { instanceId, message = "No sealed transactions found for this instance — nothing to rebuild." })
        : Results.Ok(rebuilt);
})
    .WithName("RebuildInstance")
    .WithSummary("Rebuild instance from the ledger")
    .WithDescription("Operator repair: reconstructs the instance projection from the register's sealed transactions and overwrites the materialized view (Feature 145 US4).");

// <summary>
// List workflow instances for the authenticated user's wallet
// </summary>
// <summary>
// Create a new workflow instance
// </summary>
instancesGroup.MapPost("/", async (
    CreateInstanceRequest request,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore,
    IBlueprintStore blueprintStore,
    IPublishedBlueprintStore publishedBlueprintStore,
    Sorcha.ServiceClients.Register.IRegisterServiceClient registerClient) =>
{
    try
    {
        // Resolve the blueprint. The draft/editable store only exists on the node that
        // authored the blueprint; a replica (Feature 137 / C1) holds it solely in the
        // published (replicated) store. Try the draft store first, then fall back to the
        // latest published version so instance creation works on a node that does not own
        // the register.
        var blueprint = await blueprintStore.GetAsync(request.BlueprintId);
        var resolvedVersion = 1;
        if (blueprint == null)
        {
            var publishedVersions = await publishedBlueprintStore.GetVersionsAsync(request.BlueprintId);
            var latest = PublishedBlueprintSelector.SelectLatest(publishedVersions);
            if (latest != null)
            {
                blueprint = latest.Blueprint;
                resolvedVersion = latest.Version;
            }
        }

        if (blueprint == null)
        {
            // Not in either store. On a replica this usually means the register's blueprints
            // have not finished replicating yet (event-driven recovery is in flight), so
            // surface a typed, retryable state rather than a bare 400 "not found".
            return Results.Json(
                new
                {
                    error = "blueprint_not_available",
                    blueprintId = request.BlueprintId,
                    registerId = request.RegisterId,
                    message = "Blueprint is not available on this node yet. If the register was recently synced, retry shortly."
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        // Feature 195 — instance creation does NOT publish.
        //
        // This block used to push the blueprint to the register on the owner node, "so action 0 has
        // something to chain from". It pushed the DRAFT, unflattened, with its own serializer
        // options, while PublishService pushes a $ref-flattened deep-copied snapshot — two different
        // shapes of one blueprint reaching the ledger depending on which path ran (#1570).
        //
        // The divergence was invisible only because the old publish txId was version-blind, so the
        // second push deduped into the first and was silently discarded. Content-addressing the
        // publication unmasks it: both shapes would land under different ids and recovery would
        // faithfully restore BOTH as distinct definitions — a silent fork replacing a silent drop,
        // and worse, because a forked definition looks healthy.
        //
        // There is now ONE writer: POST /api/blueprints/{id}/publish. An instance is created against
        // a definition that has already been published, and pins to it; the starting action chains
        // from that definition's own publication transaction, read from the instance's pin rather
        // than recomputed. Nothing here needs to write to the ledger.

        // Find starting actions        // Find starting actions
        var startingActions = blueprint.Actions
            .Where(a => a.IsStartingAction)
            .Select(a => a.Id)
            .ToList();

        if (startingActions.Count == 0)
        {
            // Default to first action if none marked as starting
            startingActions = [blueprint.Actions.First().Id];
        }

        // Pre-populate participant wallets from blueprint definitions so that
        // pending action queries can match by wallet address before the participant
        // has executed their first action on this instance.
        var participantWallets = new Dictionary<string, string>();
        foreach (var p in blueprint.Participants.Where(p => !string.IsNullOrEmpty(p.WalletAddress)))
        {
            participantWallets[p.Id] = p.WalletAddress!;
        }

        // Feature 194/195: choose the definition this instance will run for its whole life.
        //
        // The pin MUST be a PUBLISHED definition, never the draft. The validator and the engine both
        // resolve published definitions, so pinning to a draft — which may differ from anything ever
        // published — would produce an instance whose every action refers to a definition no node can
        // resolve. That is why the pin is taken from the published store even when the draft store
        // answered above: what runs is what was published, not what is in an editor.
        //
        // Feature 195 makes the pin the PUBLICATION TRANSACTION ID rather than an executable-
        // definition hash. It names a ledger fact, so any node holding the register can resolve it —
        // and the starting action chains from that very transaction, which is why anchor and pin are
        // now one value.
        var latestPublished = PublishedBlueprintSelector.SelectLatest(
            await publishedBlueprintStore.GetVersionsAsync(request.BlueprintId));

        var pinnedExecDefHash = latestPublished?.PublicationTxId ?? string.Empty;
        if (latestPublished is not null)
        {
            resolvedVersion = latestPublished.Version;
        }
        else
        {
            // No published version on this node. The blueprint is pushed to the register a few
            // lines above by the owner, and recovery will surface it later, so the instance is
            // still viable — but it starts unpinned and folds through the pre-feature fallback.
            // Say so loudly: a silent empty pin is indistinguishable from the defect this feature
            // exists to remove.
            logger.LogWarning(
                "Instance for blueprint {BlueprintId} on register {RegisterId} is being created with NO " +
                "pinned definition — no published version is resolvable on this node. It will fall back " +
                "to the latest definition when folded.",
                request.BlueprintId, request.RegisterId);
        }

        // Feature 195 (FR-009) — INITIALISE FROM THE DEFINITION WE PIN.
        //
        // The starting actions, participant wallets and title above were derived from whatever the
        // draft store answered with. Pinning a different definition than the instance was set up from
        // is how an instance could be born already inconsistent: its current-action set and
        // pre-bound wallets from one definition, every subsequent action validated against another.
        // One definition, used for both.
        if (latestPublished?.Blueprint is { } pinnedDefinition)
        {
            blueprint = pinnedDefinition;

            startingActions = pinnedDefinition.Actions
                .Where(a => a.IsStartingAction)
                .Select(a => a.Id)
                .ToList();
            if (startingActions.Count == 0 && pinnedDefinition.Actions.Count > 0)
            {
                startingActions = [pinnedDefinition.Actions.First().Id];
            }

            participantWallets = pinnedDefinition.Participants
                .Where(pp => !string.IsNullOrEmpty(pp.WalletAddress))
                .ToDictionary(pp => pp.Id, pp => pp.WalletAddress!);
        }

        // Create instance
        var instance = new Sorcha.Blueprint.Service.Models.Instance
        {
            Id = Guid.NewGuid().ToString(),
            BlueprintId = request.BlueprintId,
            BlueprintVersion = resolvedVersion,
            BlueprintDefinitionTxId = pinnedExecDefHash,
            RegisterId = request.RegisterId,
            CurrentActionIds = startingActions,
            ParticipantWallets = participantWallets,
            State = Sorcha.Blueprint.Service.Models.InstanceState.Active,
            TenantId = request.TenantId ?? "default",
            Metadata = request.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "")
                ?? new Dictionary<string, string>()
        };
        // Ensure BlueprintTitle is stored as human-readable title, not the blueprint ID
        if (!instance.Metadata.ContainsKey("BlueprintTitle"))
        {
            instance.Metadata["BlueprintTitle"] = blueprint.Title;
        }

        await instanceStore.CreateAsync(instance);

        return Results.Created($"/api/instances/{instance.Id}", instance);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("CreateInstance")
.WithSummary("Create workflow instance")
.WithDescription("Create a new workflow instance for a published blueprint");

// Issue #1182 — GET /{instanceId}, /{instanceId}/state and /{instanceId}/next-actions. Previously
// three inline lambdas here, each returning instance content to ANY authenticated caller; now
// participant-gated handlers in Sorcha.Blueprint.Service.Endpoints.InstanceReadEndpoints.
instancesGroup.MapInstanceReadEndpoints();

// Feature 186 (#1163) — the citizen "My Applications" read surface. A SIBLING of /api/instances,
// not a reshaping of it: the citizen wallet app binds GET /api/instances/{id}, so that group keeps
// its raw-model shape and this one carries the citizen projection.
app.MapMeApplicationEndpoints();

// P0 fix (fix/pwa-p0-claim-and-camera) — GET /{instanceId}/actions/{actionId}: instance-scoped,
// consumer-readable action schema (see Sorcha.Blueprint.Service.Endpoints.InstanceActionEndpoints).
instancesGroup.MapInstanceActionSchemaEndpoint();

// <summary>
// Execute an action in a workflow instance (with orchestration)
// </summary>
instancesGroup.MapPost("/{instanceId}/actions/{actionId}/execute", async (
    HttpContext context,
    string instanceId,
    int actionId,
    Sorcha.Blueprint.Service.Models.Requests.ActionSubmissionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService actionExecutionService,
    Sorcha.Blueprint.Service.Endpoints.FileUploadSessionStore fileSessionStore) =>
{
    try
    {
        // [085] Inject file upload master keys into payload before execution
        if (request.PayloadData != null)
        {
            foreach (var kvp in request.PayloadData.ToList())
            {
                if (kvp.Value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (el.TryGetProperty("chunkTransactionIds", out _) &&
                        el.TryGetProperty("masterKeyId", out var mkId) &&
                        mkId.GetString() == "server-managed")
                    {
                        string? sessionId = null;
                        if (el.TryGetProperty("uploadSessionId", out var sid))
                            sessionId = sid.GetString();

                        if (sessionId != null && fileSessionStore.TryGetSession(sessionId, out var masterKey, out _))
                        {
                            var rawJson = el.GetRawText();
                            var keyB64 = Convert.ToBase64String(masterKey);
                            var injected = rawJson.TrimEnd('}') +
                                ",\"masterKeyBase64\":\"" + keyB64 + "\"}";
                            injected = injected.Replace("\"server-managed\"", "\"embedded\"");
                            injected = System.Text.RegularExpressions.Regex.Replace(
                                injected, ",?\"uploadSessionId\":\"[^\"]*\"", "");
                            using var doc = System.Text.Json.JsonDocument.Parse(injected);
                            request.PayloadData[kvp.Key] = doc.RootElement.Clone();
                            logger.LogInformation("[085] Injected master file key into field '{Field}' for action {ActionId}", kvp.Key, actionId);
                        }
                        else
                        {
                            logger.LogWarning("[085] Upload session {SessionId} not found for field '{Field}'", sessionId, kvp.Key);
                        }
                    }
                }
            }
        }

        // Get delegation token from context (set by middleware)
        var delegationToken = context.Items["DelegationToken"] as string;
        if (string.IsNullOrEmpty(delegationToken))
        {
            return Results.BadRequest(new { error = "X-Delegation-Token header is required for action execution" });
        }

        var response = await actionExecutionService.ExecuteAsync(
            instanceId,
            actionId,
            request,
            delegationToken,
            context.User);

        // Feature 111: awaiting presentation = 202 Accepted (action not yet complete).
        if (response.AwaitingPresentation)
        {
            return Results.Accepted($"/api/presentations/{response.PresentationRequest?.RequestId}/status", response);
        }

        return Results.Ok(response);
    }
    catch (PresentationRateLimitedException ex)
    {
        if (ex.RetryAfter is { } retry)
        {
            context.Response.Headers["Retry-After"] = ((int)Math.Ceiling(retry.TotalSeconds)).ToString();
        }
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (PresentationAlreadyCompleteException ex)
    {
        // Feature 111 US3 — retry gate: action already has a successful outcome.
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(ex.Message, statusCode: 403);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithRequestValidation()
.WithName("ExecuteAction")
.WithSummary("Execute action with orchestration")
.WithDescription("Execute an action in a workflow instance with full orchestration: state reconstruction, validation, routing, transaction building, and notification. Requires X-Delegation-Token header.");

// <summary>
// Reject an action in a workflow instance
// </summary>
instancesGroup.MapPost("/{instanceId}/actions/{actionId}/reject", async (
    HttpContext context,
    string instanceId,
    int actionId,
    Sorcha.Blueprint.Service.Models.Requests.ActionRejectionRequest request,
    Sorcha.Blueprint.Service.Services.Interfaces.IActionExecutionService actionExecutionService) =>
{
    try
    {
        // Get delegation token from context (set by middleware)
        var delegationToken = context.Items["DelegationToken"] as string;
        if (string.IsNullOrEmpty(delegationToken))
        {
            return Results.BadRequest(new { error = "X-Delegation-Token header is required for action rejection" });
        }

        var response = await actionExecutionService.RejectAsync(
            instanceId,
            actionId,
            request,
            delegationToken,
            context.User);

        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(ex.Message, statusCode: 403);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Request failed");
        return Results.Problem("An error occurred processing the request.", statusCode: 400);
    }
})
.WithName("RejectActionInInstance")
.WithSummary("Reject action in workflow")
.WithDescription("Reject an action in a workflow instance, routing to the configured rejection target. Requires X-Delegation-Token header.");

// ===========================
// Health & Status Endpoints
// ===========================

app.MapGet("/api/health", async (IBlueprintStore blueprintStore, IPublishedBlueprintStore publishedStore, Sorcha.Blueprint.Service.Services.IStatusListManager statusListManager, Sorcha.Blueprint.Service.Models.RecoveryState recoveryState) =>
{
    // Gate: return 503 while recovering from ledger
    if (!recoveryState.IsComplete)
    {
        var onlineCount = recoveryState.RegisterStates.Values.Count(r => r.Status == Sorcha.Blueprint.Service.Models.RegisterHealthStatus.Online);
        var offlineCount = recoveryState.RegisterStates.Values.Count(r => r.Status == Sorcha.Blueprint.Service.Models.RegisterHealthStatus.Offline);
        var pendingCount = recoveryState.RegisterStates.Values.Count(r => r.Status == Sorcha.Blueprint.Service.Models.RegisterHealthStatus.Unknown);

        return Results.Json(new
        {
            status = "recovering",
            service = "blueprint-service",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0",
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
            recovery = new
            {
                startedAt = recoveryState.StartedAt,
                registersTotal = recoveryState.RegisterStates.Count,
                registersRecovered = onlineCount,
                registersOffline = offlineCount,
                registersPending = pendingCount
            }
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        var blueprints = await blueprintStore.GetAllAsync();
        var blueprintCount = blueprints.Count();

        // Count published blueprints
        var publishedCount = 0;
        foreach (var blueprint in blueprints)
        {
            var versions = await publishedStore.GetVersionsAsync(blueprint.Id);
            publishedCount += versions.Count();
        }

        // Probe status list availability
        var statusListAvailable = false;
        try
        {
            // Attempt to fetch a known list; null result is fine (no lists yet)
            await statusListManager.GetListAsync("health-probe");
            statusListAvailable = true;
        }
        catch
        {
            // Status list subsystem unavailable
        }

        var regOnline = recoveryState.RegisterStates.Values.Count(r => r.Status == Sorcha.Blueprint.Service.Models.RegisterHealthStatus.Online);
        var regOffline = recoveryState.RegisterStates.Values.Count(r => r.Status == Sorcha.Blueprint.Service.Models.RegisterHealthStatus.Offline);

        return Results.Ok(new
        {
            status = "healthy",
            service = "blueprint-service",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0",
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
            metrics = new
            {
                totalBlueprints = blueprintCount,
                publishedVersions = publishedCount,
                statusListAvailable
            },
            registers = new
            {
                total = recoveryState.RegisterStates.Count,
                online = regOnline,
                offline = regOffline,
                lastRefresh = recoveryState.CompletedAt
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Health check failed");
        return Results.Json(new
        {
            status = "unhealthy",
            service = "blueprint-service",
            timestamp = DateTimeOffset.UtcNow,
            error = "Service unavailable"
        }, statusCode: 503);
    }
})
.WithName("HealthCheck")
.WithSummary("Service health check with metrics")
.WithTags("Health")
.AllowAnonymous();

// ===========================
// Statistics Endpoint (public, no auth)
// ===========================

app.MapGet("/api/stats", async (
    IBlueprintStore blueprintStore,
    Sorcha.Blueprint.Service.Storage.IInstanceStore instanceStore) =>
{
    try
    {
        var blueprints = await blueprintStore.GetAllAsync();
        var blueprintCount = blueprints.Count();
        var instanceCount = await instanceStore.CountAsync();
        var activeInstanceCount = await instanceStore.CountByStateAsync(
            Sorcha.Blueprint.Service.Models.InstanceState.Active);

        return Results.Ok(new
        {
            blueprintCount,
            instanceCount,
            activeInstanceCount
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to get blueprint statistics");
        return Results.Ok(new
        {
            blueprintCount = 0,
            instanceCount = 0,
            activeInstanceCount = 0
        });
    }
})
.WithName("GetBlueprintStats")
.WithSummary("Get blueprint statistics (public)")
.WithDescription("Returns aggregate counts of blueprints and instances. No authentication required.")
.WithTags("Statistics")
.AllowAnonymous();

app.Run();

// ===========================
// Service Interfaces & Implementations
// ===========================

/// <summary>
/// Blueprint storage interface
/// </summary>
public interface IBlueprintStore
{
    Task<BlueprintModel?> GetAsync(string id);
    Task<IEnumerable<BlueprintModel>> GetAllAsync();
    Task<IEnumerable<BlueprintModel>> GetAllByOrgAsync(string organizationId);
    Task<BlueprintModel> AddAsync(BlueprintModel blueprint);
    Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint);
    Task<bool> DeleteAsync(string id);
}

/// <summary>
/// Published blueprint storage interface
/// </summary>
public interface IPublishedBlueprintStore
{
    Task<PublishedBlueprint> AddAsync(PublishedBlueprint published);
    Task<PublishedBlueprint?> GetVersionAsync(string blueprintId, int version);
    Task<IEnumerable<PublishedBlueprint>> GetVersionsAsync(string blueprintId);
    Task<IEnumerable<PublishedBlueprint>> GetByRegisterAsync(string registerId);

    /// <summary>
    /// Feature 194 — resolves the one definition an instance is pinned to.
    /// </summary>
    /// <remarks>
    /// This is the resolution the execution path must use. <see cref="GetVersionAsync"/> resolves by
    /// ordinal, which is assigned from insert order and re-derived on recovery, so it cannot be
    /// relied on to denote the same definition twice.
    /// </remarks>
    /// <returns>The pinned definition, or <c>null</c> if this node cannot resolve it — which the
    /// caller MUST treat as a refusal, never as licence to fall back to the latest.</returns>
    Task<PublishedBlueprint?> GetByPublicationAsync(string blueprintId, string execDefHash);

    /// <summary>Feature 154 (catalogue) — the latest published version of every blueprint.</summary>
    Task<IEnumerable<PublishedBlueprint>> GetAllLatestAsync();
}

/// <summary>
/// Blueprint service interface
/// </summary>
public interface IBlueprintService
{
    Task<PagedResult<BlueprintSummary>> GetAllAsync(int page, int pageSize, string? search, string? status, string? organizationId = null);
    Task<BlueprintModel?> GetByIdAsync(string id, string? organizationId = null);
    Task<BlueprintModel> CreateAsync(BlueprintModel blueprint, string? organizationId = null);
    Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint, string? organizationId = null);
    Task<bool> DeleteAsync(string id, string? organizationId = null);
}

/// <summary>
/// Publish service interface
/// </summary>
public interface IPublishService
{
    Task<PublishResult> PublishAsync(string blueprintId, string registerId);
    Task<BlueprintValidationResult> ValidateAsync(string blueprintId);
}

/// <summary>
/// Validation-only result (no publishing side effects)
/// </summary>
public record BlueprintValidationResult(
    string BlueprintId,
    string Title,
    bool IsValid,
    List<ValidationIssueDto> ValidationResults,
    List<string> Warnings);

public record ValidationIssueDto(string Severity, string Message, string? Location = null);

/// <summary>
/// In-memory blueprint store
/// </summary>
public class InMemoryBlueprintStore : IBlueprintStore
{
    private readonly ConcurrentDictionary<string, BlueprintModel> _blueprints = new();

    public Task<BlueprintModel?> GetAsync(string id)
    {
        _blueprints.TryGetValue(id, out var blueprint);
        return Task.FromResult(blueprint);
    }

    public Task<IEnumerable<BlueprintModel>> GetAllAsync()
    {
        return Task.FromResult(_blueprints.Values.AsEnumerable());
    }

    public Task<IEnumerable<BlueprintModel>> GetAllByOrgAsync(string organizationId)
    {
        return Task.FromResult(_blueprints.Values
            .Where(b => b.OrganizationId == organizationId)
            .AsEnumerable());
    }

    public Task<BlueprintModel> AddAsync(BlueprintModel blueprint)
    {
        blueprint.Id = Guid.NewGuid().ToString();
        blueprint.CreatedAt = DateTimeOffset.UtcNow;
        blueprint.UpdatedAt = DateTimeOffset.UtcNow;
        _blueprints[blueprint.Id] = blueprint;
        return Task.FromResult(blueprint);
    }

    public Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint)
    {
        if (!_blueprints.ContainsKey(id)) return Task.FromResult<BlueprintModel?>(null);

        blueprint.Id = id;
        blueprint.UpdatedAt = DateTimeOffset.UtcNow;
        _blueprints[id] = blueprint;
        return Task.FromResult<BlueprintModel?>(blueprint);
    }

    public Task<bool> DeleteAsync(string id)
    {
        return Task.FromResult(_blueprints.TryRemove(id, out _));
    }
}

/// <summary>
/// In-memory published blueprint store
/// </summary>
public class InMemoryPublishedBlueprintStore : IPublishedBlueprintStore
{
    private readonly ConcurrentDictionary<string, List<PublishedBlueprint>> _published = new();

    public Task<PublishedBlueprint> AddAsync(PublishedBlueprint published)
    {
        var versions = _published.GetOrAdd(published.BlueprintId, _ => []);
        published.Version = versions.Count + 1;
        published.PublishedAt = DateTimeOffset.UtcNow;
        versions.Add(published);
        return Task.FromResult(published);
    }

    public Task<PublishedBlueprint?> GetVersionAsync(string blueprintId, int version)
    {
        if (_published.TryGetValue(blueprintId, out var versions))
        {
            return Task.FromResult(versions.FirstOrDefault(v => v.Version == version));
        }
        return Task.FromResult<PublishedBlueprint?>(null);
    }

    public Task<IEnumerable<PublishedBlueprint>> GetVersionsAsync(string blueprintId)
    {
        if (_published.TryGetValue(blueprintId, out var versions))
        {
            return Task.FromResult(versions.AsEnumerable());
        }
        return Task.FromResult(Enumerable.Empty<PublishedBlueprint>());
    }

    /// <inheritdoc/>
    public Task<PublishedBlueprint?> GetByPublicationAsync(string blueprintId, string publicationTxId)
    {
        if (string.IsNullOrWhiteSpace(publicationTxId) || !_published.TryGetValue(blueprintId, out var versions))
        {
            return Task.FromResult<PublishedBlueprint?>(null);
        }

        // Feature 195 — NO TIE-BREAK. A publication id identifies exactly one publication, so there
        // is nothing to break a tie between.
        //
        // What was here resolved by ExecDefHash and, on a collision, took the highest ordinal —
        // justified by a comment claiming colliding entries were "the same definition by
        // construction". That premise was FALSE: the executable-definition projection omitted nine
        // execution-affecting fields, so two publications sharing a hash could differ in rejection
        // routing, legacy participant routing, branch deadlines, decision-notice wording,
        // presentation config and instance references. For exactly those fields a pinned instance
        // was handed the NEWEST definition — the defect version pinning exists to remove,
        // reappearing inside its own resolution path.
        var match = versions
            .FirstOrDefault(v => v.Blueprint is not null
                                 && string.Equals(v.PublicationTxId, publicationTxId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<IEnumerable<PublishedBlueprint>> GetByRegisterAsync(string registerId)
    {
        var result = _published.Values
            .SelectMany(versions => versions)
            .Where(p => string.Equals(p.RegisterId, registerId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.BlueprintId)
            .Select(g => g.OrderByDescending(v => v.Version).First())
            .ToList();
        return Task.FromResult<IEnumerable<PublishedBlueprint>>(result);
    }

    public Task<IEnumerable<PublishedBlueprint>> GetAllLatestAsync()
    {
        var result = _published.Values
            .Where(versions => versions.Count > 0)
            .Select(versions => versions.OrderByDescending(v => v.Version).First())
            .ToList();
        return Task.FromResult<IEnumerable<PublishedBlueprint>>(result);
    }
}

/// <summary>
/// Blueprint service implementation
/// </summary>
public class BlueprintService(IBlueprintStore store) : IBlueprintService
{
    private readonly IBlueprintStore _store = store;

    public async Task<PagedResult<BlueprintSummary>> GetAllAsync(int page, int pageSize, string? search, string? status, string? organizationId = null)
    {
        var allBlueprints = !string.IsNullOrEmpty(organizationId)
            ? await _store.GetAllByOrgAsync(organizationId)
            : await _store.GetAllAsync();

        // Apply filtering
        var filtered = allBlueprints.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(b =>
                b.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (b.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var total = filtered.Count();
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BlueprintSummary
            {
                Id = b.Id,
                Title = b.Title,
                Description = b.Description,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                ParticipantCount = b.Participants.Count,
                ActionCount = b.Actions.Count
            })
            .ToList();

        return new PagedResult<BlueprintSummary>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<BlueprintModel?> GetByIdAsync(string id, string? organizationId = null)
    {
        var blueprint = await _store.GetAsync(id);
        if (blueprint == null) return null;

        // Enforce org ownership if organizationId is provided
        if (!string.IsNullOrEmpty(organizationId) && blueprint.OrganizationId != organizationId)
            return null;

        return blueprint;
    }

    public Task<BlueprintModel> CreateAsync(BlueprintModel blueprint, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
            blueprint.OrganizationId = organizationId;

        return _store.AddAsync(blueprint);
    }

    public async Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
        {
            var existing = await _store.GetAsync(id);
            if (existing == null || existing.OrganizationId != organizationId)
                return null;
        }

        return await _store.UpdateAsync(id, blueprint);
    }

    public async Task<bool> DeleteAsync(string id, string? organizationId = null)
    {
        if (!string.IsNullOrEmpty(organizationId))
        {
            var existing = await _store.GetAsync(id);
            if (existing == null || existing.OrganizationId != organizationId)
                return false;
        }

        return await _store.DeleteAsync(id);
    }
}

/// <summary>
/// Publish service implementation with validation
/// </summary>
public class PublishService(
    IBlueprintStore blueprintStore,
    IPublishedBlueprintStore publishedStore,
    Sorcha.ServiceClients.Register.IRegisterServiceClient? registerClient = null,
    StackExchange.Redis.IConnectionMultiplexer? redis = null,
    Sorcha.Blueprint.Service.Services.ISchemaRefResolver? schemaRefResolver = null,
    ILogger<PublishService>? logger = null,
    Sorcha.Blueprint.Engine.Implementation.ExecutableDefinitionHasher? execDefHasher = null) : IPublishService
{
    private readonly IBlueprintStore _blueprintStore = blueprintStore;
    private readonly IPublishedBlueprintStore _publishedStore = publishedStore;
    private readonly Sorcha.ServiceClients.Register.IRegisterServiceClient? _registerClient = registerClient;
    private readonly StackExchange.Redis.IConnectionMultiplexer? _redis = redis;
    private readonly Sorcha.Blueprint.Service.Services.ISchemaRefResolver? _schemaRefResolver = schemaRefResolver;
    private readonly ILogger<PublishService>? _logger = logger;

    // Feature 194: the executable-definition hash is the pin an instance runs against, so it must be
    // computed here — the one place that knows the exact bytes being stored, cached and pushed.
    private readonly Sorcha.Blueprint.Engine.Implementation.ExecutableDefinitionHasher _execDefHasher =
        execDefHasher ?? new Sorcha.Blueprint.Engine.Implementation.ExecutableDefinitionHasher();

    /// <summary>
    /// Walks every action's <c>DataSchemas</c> and replaces each
    /// <see cref="System.Text.Json.JsonDocument"/> with a flattened version
    /// where every Sorcha core <c>$ref</c> has been inlined. After this runs
    /// the validator and the form renderer never see a primitive reference —
    /// they see the fully-resolved schema. Mutates the supplied blueprint
    /// in place.
    /// </summary>
    /// <remarks>
    /// Called once at publish time so the immutable published snapshot
    /// captures the flat form. The draft store is unchanged because the
    /// caller does not write the mutated blueprint back. Failures bubble up
    /// as <see cref="Sorcha.Blueprint.Service.Services.SchemaRefResolutionException"/>
    /// and are converted to publish-time errors by the caller.
    /// </remarks>
    private void FlattenActionSchemas(BlueprintModel blueprint)
    {
        if (_schemaRefResolver is null) return;

        var anyActionRewritten = false;

        foreach (var action in blueprint.Actions)
        {
            if (action.DataSchemas is null) continue;

            // Short-circuit: scan the raw JSON for "$ref" before paying the
            // parse → flatten → re-serialise round trip. Blueprints that
            // predate the primitive library (the vast majority) skip the
            // resolver entirely and keep their existing JsonDocument
            // instances, avoiding both the CPU cost and the IDisposable
            // churn on every publish.
            var schemaList = action.DataSchemas.ToList();
            var actionNeedsFlatten = schemaList.Any(doc =>
                doc.RootElement.GetRawText().Contains("\"$ref\"", StringComparison.Ordinal));

            if (!actionNeedsFlatten) continue;

            // Build the replacement list alongside a parallel track of the
            // superseded originals so we can dispose exactly the right set.
            // A null-node parse result (literal JSON `null` — pathological
            // input) is passed through unchanged; we track it specially so
            // we do NOT dispose the original in that case.
            var flattened = new List<System.Text.Json.JsonDocument>(schemaList.Count);
            var disposedOriginals = new List<System.Text.Json.JsonDocument>(schemaList.Count);
            foreach (var schemaDoc in schemaList)
            {
                var raw = schemaDoc.RootElement.GetRawText();
                var node = System.Text.Json.Nodes.JsonNode.Parse(raw);
                if (node is null)
                {
                    flattened.Add(schemaDoc); // carried through — do NOT dispose
                    continue;
                }

                var flatNode = _schemaRefResolver.Flatten(node);
                flattened.Add(System.Text.Json.JsonDocument.Parse(flatNode.ToJsonString()));
                disposedOriginals.Add(schemaDoc);
            }

            // Dispose superseded JsonDocuments before losing the last
            // reference — JsonDocument owns pooled byte buffers.
            foreach (var old in disposedOriginals)
            {
                old.Dispose();
            }

            action.DataSchemas = flattened;
            anyActionRewritten = true;
        }

        if (anyActionRewritten)
        {
            _logger?.LogDebug(
                "Flattened core primitive $refs in blueprint {BlueprintId}", blueprint.Id);
        }
    }

    public async Task<BlueprintValidationResult> ValidateAsync(string blueprintId)
    {
        var blueprint = await _blueprintStore.GetAsync(blueprintId);
        if (blueprint is null)
        {
            return new BlueprintValidationResult(
                blueprintId,
                string.Empty,
                false,
                [new ValidationIssueDto("error", "Blueprint not found")],
                []);
        }

        var (errors, warnings) = ValidateBlueprint(blueprint);
        var issues = errors.Select(e => new ValidationIssueDto("error", e)).ToList();

        return new BlueprintValidationResult(
            blueprintId,
            blueprint.Title ?? string.Empty,
            errors.Count == 0,
            issues,
            warnings);
    }

    public async Task<PublishResult> PublishAsync(string blueprintId, string registerId)
    {
        var blueprint = await _blueprintStore.GetAsync(blueprintId);
        if (blueprint is null)
        {
            return PublishResult.Failed("Blueprint not found");
        }

        // Feature 103 T041: flatten Sorcha core primitive $refs BEFORE
        // validating, storing, or pushing to the register. After this step
        // every action's dataSchema is fully self-contained — no downstream
        // consumer (validator / register / form renderer) needs to know
        // about the primitive library. A failure here surfaces as a
        // publish-time error pointing at the unresolvable URI.
        try
        {
            FlattenActionSchemas(blueprint);
        }
        catch (Sorcha.Blueprint.Service.Services.SchemaRefResolutionException ex)
        {
            _logger?.LogWarning(ex,
                "Blueprint {BlueprintId} failed schema $ref flattening at publish time", blueprintId);
            return PublishResult.Failed(
                $"Schema $ref resolution failed: {ex.Message}" +
                (ex.RefUri is not null ? $" (offending $ref: {ex.RefUri})" : string.Empty));
        }

        // Validate blueprint — cycle detections are warnings, not errors
        var (errors, warnings) = ValidateBlueprint(blueprint);
        if (errors.Count > 0)
        {
            return PublishResult.Failed(errors.ToArray());
        }

        // Set hasCycles metadata if cycle warnings were detected
        if (warnings.Count > 0)
        {
            blueprint.Metadata ??= new Dictionary<string, string>();
            blueprint.Metadata["hasCycles"] = "true";
        }

        // Feature 194: take a genuine DEEP COPY for the published snapshot, and compute the
        // executable-definition hash over it.
        //
        // This used to store `blueprint` — the very object `_blueprintStore.GetAsync` returned,
        // which for the in-memory store IS the stored draft. The "immutable snapshot" comment was
        // therefore false: FlattenActionSchemas mutates it in place, the hasCycles write above
        // mutates it again, and any later in-place edit of the draft would silently rewrite this
        // published version too. That is fatal to content-addressing — a pin is a promise that an
        // identifier always denotes the same bytes, and it is void if the bytes can change under it.
        //
        // The round trip through JSON is both the copy and the canonical bytes: `blueprintJson` is
        // what is pushed to the register below, so the stored copy, the hash and the ledger record
        // are provably the same content. Order is load-bearing — this must run AFTER $ref flattening
        // and AFTER the hasCycles write, or the hash addresses a definition nothing ever stores.
        var blueprintJson = System.Text.Json.JsonSerializer.Serialize(blueprint);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<BlueprintModel>(blueprintJson)
            ?? throw new InvalidOperationException(
                $"Blueprint {blueprintId} could not be round-tripped for publication.");

        var execDefHash = _execDefHasher.ComputeHash(snapshot);

        // Feature 195 — the LEDGER FIRST, because the ledger assigns the identity.
        //
        // This ordering is load-bearing. The register is the one producer of a definition's
        // publication id, so the local store cannot record a definition until the register has said
        // what that definition IS. It also fixes a quieter problem in the old order: the published
        // store was written before the ledger push, so a failed push left a definition that existed
        // locally and nowhere else — resolvable on this node, unresolvable on every other, and
        // indistinguishable from a healthy publish until something needed it.
        if (_registerClient is null)
        {
            return PublishResult.Failed(
                "Cannot publish: no register client is configured, so no definition identity can be assigned.");
        }

        var publication = await _registerClient.PublishBlueprintToRegisterAsync(
            registerId, blueprintId, blueprintJson, "system");

        if (publication is null)
        {
            return PublishResult.Failed(
                $"Register did not accept blueprint {blueprintId} for register {registerId}. " +
                "Nothing was recorded locally — a definition that exists only on this node is worse " +
                "than a failed publish, because it looks resolvable here and is not resolvable anywhere else.");
        }

        // Create published version (immutable snapshot), carrying the identity the register assigned.
        var published = new PublishedBlueprint
        {
            BlueprintId = blueprint.Id,
            Blueprint = snapshot,
            PublicationTxId = publication.PublicationTxId,
            ExecDefHash = execDefHash,
            PublishedAt = DateTimeOffset.UtcNow,
            RegisterId = registerId
        };

        await _publishedStore.AddAsync(published);

        _logger?.LogInformation(
            "Blueprint {BlueprintId} published to register {RegisterId} as {PublicationTxId} " +
            "(execDefHash {ExecDefHash}, alreadyPublished: {AlreadyPublished})",
            blueprintId, registerId, publication.PublicationTxId, execDefHash, publication.AlreadyPublished);

        // Populate Validator's blueprint cache in Redis so transactions referencing this blueprint pass validation
        if (_redis is not null)
        {
            try
            {
                var db = _redis.GetDatabase();
                // Feature 194: the cache is keyed by (blueprintId, execDefHash) so several
                // definitions of one blueprint coexist and a pinned instance resolves its own.
                // The format has ONE home — BlueprintCacheKey — because it previously lived here as
                // a literal AND in the validator's BlueprintCache, and re-keying one side alone
                // makes every lookup miss and silently fall through to the latest definition.
                var cacheKey = Sorcha.Blueprint.Models.BlueprintCacheKey.For(blueprintId, execDefHash);
                var cachedJson = System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
                await db.StringSetAsync(cacheKey, cachedJson);
                _logger?.LogInformation("Blueprint {BlueprintId} cached in Redis for Validator (key: {CacheKey}, no expiry — immutable)", blueprintId, cacheKey);
            }
            catch (Exception ex)
            {
                // Non-fatal: Validator will fail with VAL_SCHEMA_001 but publishing itself succeeded
                _logger?.LogWarning(ex, "Failed to cache blueprint {BlueprintId} in Redis for Validator", blueprintId);
            }
        }

        return PublishResult.Success(published, warnings.ToArray());
    }

    private (List<string> Errors, List<string> Warnings) ValidateBlueprint(BlueprintModel blueprint)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Rule 0: Title and description must meet the model's required minimum lengths
        // (BlueprintModel declares [Required] + MinLength(3)/(5), but DataAnnotations are not
        // evaluated on the publish path — only the AI-chat tool enforced this. Mirror it here so
        // a directly-authored blueprint can't be published with a missing/too-short title/description.)
        if (string.IsNullOrWhiteSpace(blueprint.Title) || blueprint.Title.Length < 3)
        {
            errors.Add("Blueprint title must be at least 3 characters");
        }

        if (string.IsNullOrWhiteSpace(blueprint.Description) || blueprint.Description.Length < 5)
        {
            errors.Add("Blueprint description must be at least 5 characters");
        }

        // Rule 1: Must have at least 2 participants
        if (blueprint.Participants.Count < 2)
        {
            errors.Add("Blueprint must have at least 2 participants");
        }

        // Rule 2: Must have at least 1 action
        if (blueprint.Actions.Count < 1)
        {
            errors.Add("Blueprint must have at least 1 action");
        }

        // Rule 3: Validate participant references across the blueprint
        var participantIds = blueprint.Participants.Select(p => p.Id).ToHashSet();
        var actionIds = blueprint.Actions.Select(a => a.Id).ToHashSet();

        foreach (var action in blueprint.Actions)
        {
            // 3a: Action.Sender must reference a valid participant
            if (!string.IsNullOrWhiteSpace(action.Sender) && !participantIds.Contains(action.Sender))
            {
                errors.Add($"Action {action.Id} ('{action.Title}'): Sender '{action.Sender}' is not a defined participant");
            }

            // 3b: Legacy participant condition principals must reference valid participants
            if (action.Participants != null)
            {
                foreach (var participant in action.Participants)
                {
                    if (!string.IsNullOrWhiteSpace(participant.Principal) && !participantIds.Contains(participant.Principal))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Participant principal '{participant.Principal}' is not defined");
                    }
                }
            }

            // 3c: Disclosure participant addresses must reference valid participants
            if (action.Disclosures != null)
            {
                foreach (var disclosure in action.Disclosures)
                {
                    if (!string.IsNullOrWhiteSpace(disclosure.ParticipantAddress) && !participantIds.Contains(disclosure.ParticipantAddress))
                    {
                        warnings.Add($"Action {action.Id} ('{action.Title}'): Disclosure references participant '{disclosure.ParticipantAddress}' which is not defined");
                    }
                }
            }

            // 3d: RejectionConfig.TargetParticipantId must reference valid participant
            if (action.RejectionConfig?.TargetParticipantId != null && !participantIds.Contains(action.RejectionConfig.TargetParticipantId))
            {
                warnings.Add($"Action {action.Id} ('{action.Title}'): Rejection target participant '{action.RejectionConfig.TargetParticipantId}' is not defined");
            }
        }

        // Rule 4: Validate credential requirements on actions (FR-020)
        foreach (var action in blueprint.Actions)
        {
            if (action.CredentialRequirements != null)
            {
                var reqIndex = 0;
                foreach (var req in action.CredentialRequirements)
                {
                    if (string.IsNullOrWhiteSpace(req.Type))
                    {
                        errors.Add($"Action {action.Id}: Credential requirement [{reqIndex}] has an empty type");
                    }

                    if (req.RequiredClaims != null)
                    {
                        foreach (var claim in req.RequiredClaims)
                        {
                            if (string.IsNullOrWhiteSpace(claim.ClaimName))
                            {
                                errors.Add($"Action {action.Id}: Credential requirement '{req.Type}' has a claim constraint with empty name");
                            }
                        }
                    }

                    reqIndex++;
                }
            }

            if (action.CredentialIssuanceConfig != null)
            {
                var config = action.CredentialIssuanceConfig;
                if (string.IsNullOrWhiteSpace(config.CredentialType))
                {
                    errors.Add($"Action {action.Id}: Credential issuance config has an empty credential type");
                }
                if (!config.ClaimMappings.Any())
                {
                    errors.Add($"Action {action.Id}: Credential issuance config must have at least one claim mapping");
                }
                if (string.IsNullOrWhiteSpace(config.RecipientParticipantId))
                {
                    errors.Add($"Action {action.Id}: Credential issuance config has an empty recipient participant ID");
                }
                else if (!participantIds.Contains(config.RecipientParticipantId))
                {
                    warnings.Add($"Action {action.Id}: Credential issuance config references participant '{config.RecipientParticipantId}' which is not defined in this blueprint");
                }
            }
        }

        // Rule 5: Detect cycles — produce warnings instead of errors
        // Cycles are valid workflow patterns (ping-pong, review loops, resubmission flows)
        var cycleDetections = DetectCycles(blueprint);
        foreach (var detection in cycleDetections)
        {
            // Rewrite cycle errors as warnings with helpful context
            var warning = detection.Replace("Circular dependency detected:", "Cyclic route detected:");
            if (!warning.Contains("loop indefinitely"))
            {
                warning += ". This blueprint will loop indefinitely unless routing conditions provide a termination path.";
            }
            warnings.Add(warning);
        }

        // Rule 6: Starting action validation
        var startingActions = blueprint.Actions.Where(a => a.IsStartingAction).ToList();
        if (startingActions.Count == 0)
        {
            warnings.Add("No action has IsStartingAction=true. The first action will be used as the implicit starting action.");
        }

        // Rule 6a (Feature 103, VAL_BP_010): Open-participant pre-binding guardrail.
        //
        // A participant referenced as the sender of an isStartingAction: true action
        // MUST have walletAddress = null in the published blueprint. The runtime
        // late-binds whichever wallet submits the first action to the participant role.
        // Pre-baking a wallet defeats the open contract and produces a misleading
        // "wallet not authorized" error at runtime from the strict equality check in
        // ActionExecutionService.cs:196-216. This rule turns the foot-gun into a
        // publish-time error.
        //
        // Contract: specs/103-verified-citizen-v2/contracts/validator-publish-errors.md
        // The canonical constant now lives in the shared Sorcha.Blueprint.Models contracts
        // project, which both this service and Validator Service reference — so the literal
        // is named once (DRIFT-003).
        const string OpenParticipantPreboundCode = Sorcha.Blueprint.Models.ValidationErrorCodes.OpenParticipantPrebound;

        foreach (var startingAction in startingActions)
        {
            if (string.IsNullOrWhiteSpace(startingAction.Sender)) continue;

            var senderParticipant = blueprint.Participants?
                .FirstOrDefault(p => string.Equals(p.Id, startingAction.Sender, StringComparison.OrdinalIgnoreCase));

            if (senderParticipant is not null &&
                !string.IsNullOrWhiteSpace(senderParticipant.WalletAddress))
            {
                errors.Add(
                    $"[{OpenParticipantPreboundCode}] Participant '{senderParticipant.Id}' is the sender of starting action " +
                    $"{startingAction.Id} ('{startingAction.Title}') and must have a null walletAddress so the " +
                    $"runtime can late-bind the first qualifying submitter to the participant role. " +
                    $"Found walletAddress: '{senderParticipant.WalletAddress}'. " +
                    $"To fix: remove the walletAddress field from the participant in the blueprint, OR " +
                    $"if the participant should NOT be open, remove isStartingAction from the action.");
            }
        }

        // Rule 7: Route targets and rejection targets must reference valid actions
        //
        // Rule 7a (Feature 104, VAL_BP_011): If a Route declares an OutputMapping,
        // every target JSON Pointer must resolve to a top-level schema field
        // declared on at least one of the route's next actions. This prevents
        // blueprint authors from writing mappings to fields that don't exist on
        // the receiving action.
        //
        // Rule 7b (Feature 104, VAL_BP_012 / WARN_BP_006): x-credential-offer
        // extension rules — it may only appear on object-typed schema fields,
        // and when present the object should declare credential_offer_uri as
        // required (warning, not error).
        const string OutputMappingTargetCode = "VAL_BP_011";
        const string CredentialOfferTypeCode = "VAL_BP_012";
        const string CredentialOfferRequiredWarning = "WARN_BP_006";

        // Walk every schema on every action looking for x-credential-offer
        foreach (var action in blueprint.Actions)
        {
            if (action.DataSchemas is null) continue;
            foreach (var schemaDoc in action.DataSchemas)
            {
                try
                {
                    var root = schemaDoc.RootElement;
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (!root.TryGetProperty("properties", out var propsEl) ||
                        propsEl.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                    foreach (var prop in propsEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                        // Skip properties that don't carry the extension
                        if (!prop.Value.TryGetProperty("x-credential-offer", out var coEl)) continue;
                        if (coEl.ValueKind != System.Text.Json.JsonValueKind.True) continue;

                        // VAL_BP_012: must be on an object-typed field
                        var typeStr = prop.Value.TryGetProperty("type", out var typeEl) &&
                                      typeEl.ValueKind == System.Text.Json.JsonValueKind.String
                            ? typeEl.GetString()
                            : null;
                        if (typeStr != "object")
                        {
                            errors.Add(
                                $"[{CredentialOfferTypeCode}] Action {action.Id} ('{action.Title}'): Field '{prop.Name}' " +
                                $"carries 'x-credential-offer: true' but is not an object-typed field (found type: '{typeStr ?? "(missing)"}'). " +
                                $"The credential-offer renderer requires an object field with credential_offer_uri inside.");
                            continue;
                        }

                        // WARN_BP_006: credential_offer_uri should be required
                        var hasRequiredUri = false;
                        if (prop.Value.TryGetProperty("required", out var requiredEl) &&
                            requiredEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var reqItem in requiredEl.EnumerateArray())
                            {
                                if (reqItem.ValueKind == System.Text.Json.JsonValueKind.String &&
                                    reqItem.GetString() == "credential_offer_uri")
                                {
                                    hasRequiredUri = true;
                                    break;
                                }
                            }
                        }
                        if (!hasRequiredUri)
                        {
                            warnings.Add(
                                $"[{CredentialOfferRequiredWarning}] Action {action.Id} ('{action.Title}'): Field '{prop.Name}' " +
                                $"is marked 'x-credential-offer: true' but does not declare 'credential_offer_uri' in its required list. " +
                                $"The credential claim card cannot render without the offer URI — add it to required to fail fast at publish time.");
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Malformed schema — other rules will surface this
                }
            }
        }

        // Rule 7d (Feature 107, Issue #337): x-review.layout typo detection.
        //
        //   WARN_BP_REVIEW_001 — non-blocking warning. SchemaLayoutParser silently falls
        //                        back to id-card when an x-review.layout value isn't in
        //                        the known set. Without this rule a blueprint author who
        //                        types "hologram" instead of "id-card" gets no signal
        //                        that their declaration was ignored.
        //
        // Implementation calls SchemaLayoutParser.EnumerateUnknownReviewLayouts so the
        // canonical layout-name set lives in exactly one place.
        foreach (var action in blueprint.Actions)
        {
            if (action.DataSchemas is null) continue;
            foreach (var schemaDoc in action.DataSchemas)
            {
                try
                {
                    foreach (var unknownLayout in
                        Sorcha.Blueprint.Models.SchemaLayoutParser.EnumerateUnknownReviewLayouts(schemaDoc.RootElement))
                    {
                        warnings.Add(
                            $"[{Sorcha.Blueprint.Models.ValidationWarningCodes.ReviewLayoutUnknown}] " +
                            $"Action {action.Id} ('{action.Title}'): x-review.layout value '{unknownLayout}' " +
                            $"is not a recognised variant. Renderer falls back to 'id-card'. " +
                            $"Known variants: {string.Join(", ", Sorcha.Blueprint.Models.SchemaLayoutParser.KnownReviewLayoutVariants)}.");
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Malformed schema — other rules will surface this
                }
            }
        }

        // Rule 7c (Feature 106): credentialIssuanceConfig.targetAudience == SorchaLocalWallet
        // guardrails for register-native credential delivery.
        //
        //   VAL_BP_CRED_001  — hard error, recipientParticipantId must resolve to a
        //                      participant declared on the blueprint. The runtime cannot
        //                      late-bind an unknown recipient at issuance time.
        //   WARN_BP_CRED_002 — non-blocking warning, action does not declare an explicit
        //                      recipient disclosure group so the engine will synthesise one
        //                      at mint time. Authors can silence by adding a disclosure.
        //   WARN_BP_CRED_005 — non-blocking warning, the action routes on a decision but declares
        //                      no issuanceCondition, so the credential is minted on every path
        //                      including the reject path (#1551).
        //   VAL_BP_CRED_003  — hard error, any action routed from a SorchaLocalWallet
        //                      issuance must have RejectionConfig.IsTerminal == true so the
        //                      holder's decline path seals a clean terminal rejection.
        //
        // Contract: specs/106-register-native-credentials/contracts/credential-issuance-config.md
        const string SorchaLocalWalletRecipientCode = Sorcha.Blueprint.Models.ValidationErrorCodes.SorchaLocalWalletRecipientUnknown;
        const string SorchaLocalWalletImplicitDisclosureWarning = Sorcha.Blueprint.Models.ValidationWarningCodes.SorchaLocalWalletImplicitDisclosure;
        const string SorchaLocalWalletRejectNotTerminalCode = Sorcha.Blueprint.Models.ValidationErrorCodes.SorchaLocalWalletRejectNotTerminal;
        // VAL_BP_CRED_004 stays local: it is emitted and named only here, so it carries no
        // cross-boundary drift risk. Promote it to the shared class if a second project ever
        // needs to name it (see the scope note on ValidationErrorCodes).
        const string CredentialVctNotAbsoluteUriCode = "VAL_BP_CRED_004";

        foreach (var action in blueprint.Actions)
        {
            var issuance = action.CredentialIssuanceConfig;
            if (issuance is null) continue;

            // VAL_BP_CRED_004 — a declared vct must be an absolute URI (SD-JWT VC vct is a URI).
            if (!string.IsNullOrWhiteSpace(issuance.Vct) && !Uri.TryCreate(issuance.Vct, UriKind.Absolute, out _))
            {
                errors.Add(
                    $"[{CredentialVctNotAbsoluteUriCode}] Action {action.Id} ('{action.Title}'): " +
                    $"credentialIssuanceConfig.vct '{issuance.Vct}' is not an absolute URI. The vct must be an " +
                    $"absolute URI, e.g. https://sorcha.dev/vc/{{type}}/v1.");
            }

            // WARN_BP_CRED_005 (#1551) — an action that models a DECISION but mints unconditionally.
            // Minting runs BEFORE routing, so a credentialIssuanceConfig mints whenever its action is
            // reached. A terminal reject route stops the credential being handed over but NOT minted
            // and delivered, so an approve/decline action with no issuanceCondition issues to the
            // rejected applicant too. Confirmed live on n1 by an A/B of two blueprints differing only
            // in issuanceCondition: with it, decision=Fail issued nothing; without it, one was minted
            // and delivered. Three shipped blueprints had this shape.
            if (issuance.IssuanceCondition is null)
            {
                var decisionRoutes = action.Routes?.ToList() ?? new List<Sorcha.Blueprint.Models.Route>();
                var conditionalRoutes = decisionRoutes.Count(r => r.Condition is not null);

                // One unconditional route is genuinely unconditional issuance, not a decision.
                if (conditionalRoutes > 0 || decisionRoutes.Count > 1)
                {
                    warnings.Add(
                        $"[{Sorcha.Blueprint.Models.ValidationWarningCodes.UnconditionalIssuanceOnDecision}] " +
                        $"Action {action.Id} ('{action.Title}'): declares credentialIssuanceConfig with no " +
                        $"issuanceCondition, but routes on a decision ({decisionRoutes.Count} route(s), " +
                        $"{conditionalRoutes} conditional). Minting runs BEFORE routing, so the credential is " +
                        $"minted and delivered on every path this action can take - including the reject path. " +
                        $"Add an issuanceCondition (JSON Logic over the submitted action data) if the credential " +
                        $"should only be issued on some outcomes.");
                }
            }

            // This block is the deprecation handler for SorchaInternal — it references the obsolete
            // value deliberately, to warn authors and to treat it as SorchaLocalWallet at runtime.
#pragma warning disable CS0618
            // SorchaInternal is deprecated — warn blueprint authors to migrate
            if (issuance.TargetAudience == Sorcha.Blueprint.Models.Credentials.TargetAudience.SorchaInternal)
            {
                warnings.Add(
                    $"[WARN_BP_CRED_DEPRECATED] Action {action.Id} ('{action.Title}'): " +
                    $"targetAudience 'SorchaInternal' is deprecated and will be removed in a future release. " +
                    $"Use 'SorchaLocalWallet' instead. SorchaInternal is treated as SorchaLocalWallet at runtime.");
            }

            // Apply SorchaLocalWallet validation to both SorchaLocalWallet and deprecated SorchaInternal
            if (issuance.TargetAudience is not (Sorcha.Blueprint.Models.Credentials.TargetAudience.SorchaLocalWallet
                or Sorcha.Blueprint.Models.Credentials.TargetAudience.SorchaInternal))
            {
#pragma warning restore CS0618
                continue;
            }

            // VAL_BP_CRED_001 — recipientParticipantId must resolve
            var recipientId = issuance.RecipientParticipantId;
            if (string.IsNullOrWhiteSpace(recipientId))
            {
                errors.Add(
                    $"[{SorchaLocalWalletRecipientCode}] Action {action.Id} ('{action.Title}'): " +
                    $"credentialIssuanceConfig.targetAudience is 'SorchaLocalWallet' but recipientParticipantId is missing. " +
                    $"Feature 106 requires the recipient participant to be declared explicitly so the engine can " +
                    $"resolve the holder wallet at mint time.");
            }
            else
            {
                var recipient = blueprint.Participants?
                    .FirstOrDefault(p => string.Equals(p.Id, recipientId, StringComparison.OrdinalIgnoreCase));
                if (recipient is null)
                {
                    errors.Add(
                        $"[{SorchaLocalWalletRecipientCode}] Action {action.Id} ('{action.Title}'): " +
                        $"credentialIssuanceConfig.recipientParticipantId '{recipientId}' does not match any " +
                        $"participant declared on this blueprint. SorchaLocalWallet delivery requires the recipient " +
                        $"to be a known participant so the engine can look up the holder wallet via late-binding.");
                }
            }

            // WARN_BP_CRED_002 — non-blocking, missing explicit recipient disclosure group
            var hasExplicitDisclosure = action.Disclosures?.Any(d =>
                d.ParticipantAddress != null &&
                string.Equals(d.ParticipantAddress, recipientId, StringComparison.OrdinalIgnoreCase)) == true;
            if (!hasExplicitDisclosure)
            {
                warnings.Add(
                    $"[{SorchaLocalWalletImplicitDisclosureWarning}] Action {action.Id} ('{action.Title}'): " +
                    $"credentialIssuanceConfig.targetAudience is 'SorchaLocalWallet' but no explicit disclosure " +
                    $"targets recipient participant '{recipientId}'. The engine will synthesise a default recipient " +
                    $"disclosure at mint time — add an explicit disclosure to silence this warning and control the " +
                    $"carried payload shape.");
            }

            // VAL_BP_CRED_003 — any action reachable from a SorchaLocalWallet issuing action via its routes
            // MUST have RejectionConfig.IsTerminal == true so the holder decline flow seals a clean terminal
            // rejection. We check each routed next action; missing or non-terminal rejection fails publish.
            var nextIds = action.Routes?.SelectMany(r => r.NextActionIds ?? Enumerable.Empty<int>()).Distinct().ToList()
                ?? new List<int>();
            foreach (var nextId in nextIds)
            {
                var nextAction = blueprint.Actions.FirstOrDefault(a => a.Id == nextId);
                if (nextAction is null) continue; // route-target check handled elsewhere
                if (nextAction.RejectionConfig is null || !nextAction.RejectionConfig.IsTerminal)
                {
                    errors.Add(
                        $"[{SorchaLocalWalletRejectNotTerminalCode}] Action {nextAction.Id} ('{nextAction.Title}'): " +
                        $"Is a routed next action from SorchaLocalWallet issuance action {action.Id} ('{action.Title}'), " +
                        $"but rejectionConfig.isTerminal is not true. The holder decline flow requires a terminal " +
                        $"rejection on the accept action — set rejectionConfig: {{ isTerminal: true }} on this action.");
                }
            }
        }

        foreach (var action in blueprint.Actions)
        {
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        foreach (var targetId in route.NextActionIds)
                        {
                            if (!actionIds.Contains(targetId))
                            {
                                errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' references non-existent action {targetId}");
                            }
                        }
                    }

                    // Rule 7a: OutputMapping target JSON Pointer must resolve to a
                    // top-level schema property on at least one next action.
                    if (route.OutputMapping != null && route.OutputMapping.Count > 0)
                    {
                        var nextIds = route.NextActionIds?.ToList() ?? new List<int>();
                        foreach (var kvp in route.OutputMapping)
                        {
                            var sourcePointer = kvp.Key;
                            var targetPointer = kvp.Value;

                            // Both pointers MUST be non-empty and start with '/' (RFC 6901)
                            if (string.IsNullOrEmpty(sourcePointer) || sourcePointer[0] != '/')
                            {
                                errors.Add(
                                    $"[{OutputMappingTargetCode}] Action {action.Id} ('{action.Title}'): Route '{route.Id}' " +
                                    $"OutputMapping has an invalid source JSON Pointer '{sourcePointer}' (must begin with '/')");
                                continue;
                            }
                            if (string.IsNullOrEmpty(targetPointer) || targetPointer[0] != '/')
                            {
                                errors.Add(
                                    $"[{OutputMappingTargetCode}] Action {action.Id} ('{action.Title}'): Route '{route.Id}' " +
                                    $"OutputMapping has an invalid target JSON Pointer '{targetPointer}' (must begin with '/')");
                                continue;
                            }

                            // Extract the top-level target field from the pointer (first segment)
                            var firstSlash = targetPointer.IndexOf('/', 1);
                            var topLevelField = firstSlash < 0
                                ? targetPointer[1..]
                                : targetPointer[1..firstSlash];
                            // RFC 6901 unescape for the top-level key
                            topLevelField = topLevelField.Replace("~1", "/").Replace("~0", "~");

                            // Check that at least one next action has this top-level field in any of its DataSchemas
                            var fieldIsDeclared = false;
                            foreach (var nextId in nextIds)
                            {
                                var nextAction = blueprint.Actions.FirstOrDefault(a => a.Id == nextId);
                                if (nextAction?.DataSchemas == null)
                                {
                                    continue;
                                }

                                foreach (var schemaDoc in nextAction.DataSchemas)
                                {
                                    try
                                    {
                                        var root = schemaDoc.RootElement;
                                        if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                            root.TryGetProperty("properties", out var propsElement) &&
                                            propsElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                                            propsElement.TryGetProperty(topLevelField, out _))
                                        {
                                            fieldIsDeclared = true;
                                            break;
                                        }
                                    }
                                    catch (System.Text.Json.JsonException)
                                    {
                                        // Malformed schema — skip; other rules will surface this
                                    }
                                }

                                if (fieldIsDeclared) break;
                            }

                            if (!fieldIsDeclared)
                            {
                                errors.Add(
                                    $"[{OutputMappingTargetCode}] Action {action.Id} ('{action.Title}'): Route '{route.Id}' " +
                                    $"OutputMapping target '{targetPointer}' refers to field '{topLevelField}' which is not declared " +
                                    $"on any DataSchema of the next action(s) [{string.Join(", ", nextIds)}]. " +
                                    $"Fix: add the field to the target action's schema, or remove the mapping entry.");
                            }
                        }
                    }
                }
            }

            if (action.RejectionConfig != null && !action.RejectionConfig.IsTerminal)
            {
                if (!actionIds.Contains(action.RejectionConfig.TargetActionId))
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Rejection target action {action.RejectionConfig.TargetActionId} does not exist");
                }
            }
        }

        // Rule 7b: Detect orphan actions (unreachable, not starting)
        var reachableActionIds = new HashSet<int>();
        foreach (var action in blueprint.Actions)
        {
            if (action.IsStartingAction || (startingActions.Count == 0 && action.Id == blueprint.Actions.Min(a => a.Id)))
            {
                reachableActionIds.Add(action.Id);
            }

            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        foreach (var targetId in route.NextActionIds)
                            reachableActionIds.Add(targetId);
                    }
                }
            }

            if (action.RejectionConfig != null)
                reachableActionIds.Add(action.RejectionConfig.TargetActionId);
        }

        foreach (var action in blueprint.Actions)
        {
            if (!reachableActionIds.Contains(action.Id))
            {
                warnings.Add($"Action {action.Id} ('{action.Title}') is unreachable — no route or rejection targets it and it is not a starting action");
            }
        }

        // Rule 8: JSON Pointer syntax validation in disclosures
        foreach (var action in blueprint.Actions)
        {
            if (action.Disclosures == null) continue;

            foreach (var disclosure in action.Disclosures)
            {
                if (disclosure.DataPointers == null || disclosure.DataPointers.Count == 0)
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Disclosure for '{disclosure.ParticipantAddress}' has no data pointers");
                    continue;
                }

                foreach (var pointer in disclosure.DataPointers)
                {
                    if (string.IsNullOrWhiteSpace(pointer))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Disclosure has an empty JSON Pointer");
                    }
                    else if (pointer != "/*" && !pointer.StartsWith("/") && !pointer.StartsWith("#/"))
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): JSON Pointer '{pointer}' must start with '/' (RFC 6901)");
                    }
                    else if (pointer.Contains("~") && !System.Text.RegularExpressions.Regex.IsMatch(pointer, @"~[01]|$"))
                    {
                        // Check for invalid escape sequences: ~ must be followed by 0 or 1
                        var segments = pointer.Split('/');
                        foreach (var segment in segments)
                        {
                            for (var ci = 0; ci < segment.Length; ci++)
                            {
                                if (segment[ci] == '~' && (ci + 1 >= segment.Length || (segment[ci + 1] != '0' && segment[ci + 1] != '1')))
                                {
                                    errors.Add($"Action {action.Id} ('{action.Title}'): JSON Pointer '{pointer}' has invalid escape '~' not followed by '0' or '1' (RFC 6901)");
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Rule 9: JSON Logic syntax validation in routes and calculations
        foreach (var action in blueprint.Actions)
        {
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.Condition != null)
                    {
                        try
                        {
                            var ruleJson = route.Condition.ToJsonString();
                            var rule = System.Text.Json.JsonSerializer.Deserialize<Json.Logic.Rule>(ruleJson);
                            if (rule == null)
                            {
                                errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' condition is not valid JSON Logic");
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"Action {action.Id} ('{action.Title}'): Route '{route.Id}' condition failed to parse as JSON Logic: {ex.Message}");
                        }
                    }
                }
            }

            if (action.Calculations != null)
            {
                foreach (var (fieldName, expression) in action.Calculations)
                {
                    try
                    {
                        var ruleJson = expression.ToJsonString();
                        var rule = System.Text.Json.JsonSerializer.Deserialize<Json.Logic.Rule>(ruleJson);
                        if (rule == null)
                        {
                            errors.Add($"Action {action.Id} ('{action.Title}'): Calculation '{fieldName}' is not valid JSON Logic");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Action {action.Id} ('{action.Title}'): Calculation '{fieldName}' failed to parse as JSON Logic: {ex.Message}");
                    }
                }
            }

            // Rule 9b: Validate Form.Schema is valid JSON Schema (if present)
            if (action.Form?.Schema != null)
            {
                try
                {
                    Json.Schema.JsonSchema.FromText(action.Form.Schema.ToJsonString());
                }
                catch (Exception ex)
                {
                    errors.Add($"Action {action.Id} ('{action.Title}'): Form schema is not valid JSON Schema: {ex.Message}");
                }
            }
        }

        return (errors, warnings);
    }

    /// <summary>
    /// Detects cycles in the blueprint action graph using DFS with coloring.
    /// Checks Routes[].NextActionIds and RejectionConfig.TargetActionId edges.
    /// </summary>
    private List<string> DetectCycles(BlueprintModel blueprint)
    {
        var errors = new List<string>();

        // Build adjacency list: actionId -> list of target actionIds
        var adjacency = new Dictionary<int, List<int>>();
        foreach (var action in blueprint.Actions)
        {
            var targets = new List<int>();

            // Add edges from Routes
            if (action.Routes != null)
            {
                foreach (var route in action.Routes)
                {
                    if (route.NextActionIds != null)
                    {
                        targets.AddRange(route.NextActionIds);
                    }
                }
            }

            // Add edge from RejectionConfig
            if (action.RejectionConfig != null)
            {
                targets.Add(action.RejectionConfig.TargetActionId);
            }

            adjacency[action.Id] = targets;
        }

        // DFS with coloring: 0=White (unvisited), 1=Gray (in path), 2=Black (done)
        var color = new Dictionary<int, int>();
        foreach (var action in blueprint.Actions)
        {
            color[action.Id] = 0;
        }

        var path = new List<int>();

        foreach (var action in blueprint.Actions)
        {
            if (color[action.Id] == 0)
            {
                DfsCycleDetect(action.Id, adjacency, color, path, errors);
            }
        }

        return errors;
    }

    private void DfsCycleDetect(
        int node,
        Dictionary<int, List<int>> adjacency,
        Dictionary<int, int> color,
        List<int> path,
        List<string> errors)
    {
        // Self-reference check
        if (adjacency.TryGetValue(node, out var neighbors) && neighbors.Contains(node))
        {
            errors.Add($"Self-referencing route detected: Action {node} routes to itself");
        }

        color[node] = 1; // Gray - in current path
        path.Add(node);

        if (neighbors != null)
        {
            foreach (var neighbor in neighbors)
            {
                if (neighbor == node) continue; // Already reported self-reference

                if (!color.ContainsKey(neighbor)) continue; // Target action doesn't exist

                if (color[neighbor] == 1)
                {
                    // Cycle detected - build cycle path string
                    var cycleStart = path.IndexOf(neighbor);
                    var cyclePath = path.Skip(cycleStart).Append(neighbor);
                    var cycleStr = string.Join(" → ", cyclePath.Select(id => $"Action {id}"));
                    errors.Add($"Circular dependency detected: {cycleStr}");
                }
                else if (color[neighbor] == 0)
                {
                    DfsCycleDetect(neighbor, adjacency, color, path, errors);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        color[node] = 2; // Black - fully explored
    }
}

// ===========================
// DTOs & Models
// ===========================

/// <summary>
/// Request body for publish to a register. Feature 142 (FR-032): an optional
/// <see cref="Override"/> overrides the rehearsal soft gate when the publishing version has not
/// been rehearsed and the caller holds register publish-governance authority.
/// </summary>
/// <param name="RegisterId">The target live register the blueprint is published to. Required.</param>
/// <param name="Override">Present only to override the rehearsal soft gate; null on a normal publish.</param>
public record PublishRequest(string RegisterId, PublishOverrideRequest? Override = null);

/// <summary>
/// Feature 142 (FR-032) — the override sub-document on a publish request. Sent only when the
/// caller intends to publish a version that has no matching rehearsal pass.
/// </summary>
/// <param name="Confirm">Must be <c>true</c> to confirm the override.</param>
/// <param name="Reason">Optional free-text reason recorded on the audit record.</param>
public record PublishOverrideRequest(bool Confirm, string? Reason = null);

/// <summary>
/// Blueprint summary for list views
/// </summary>
public record BlueprintSummary
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int ParticipantCount { get; init; }
    public int ActionCount { get; init; }
}

/// <summary>
/// Paged result wrapper
/// </summary>
public record PagedResult<T>
{
    public IEnumerable<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

/// <summary>
/// Published blueprint with version
/// </summary>
public record PublishedBlueprint
{
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>
    /// The ordinal version. <b>Display label only</b> (Feature 194 / D4): it is assigned from
    /// in-memory insert order and re-derived on recovery, so it is not a stable identifier and
    /// nothing may resolve a definition by it. Use <see cref="ExecDefHash"/> for that.
    /// </summary>
    public int Version { get; set; }

    public BlueprintModel Blueprint { get; init; } = null!;

    /// <summary>
    /// The publication transaction id — <b>this definition's identity</b> (Feature 195).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recorded from the Register Service's publish response. It is <b>never computed here</b>: that
    /// service is the one producer, and a second one would mint a plausible id that disagrees with the
    /// ledger's (gated by <c>scripts/check-publication-id-owner.ps1</c>).
    /// </para>
    /// <para>
    /// The absence of this field is what created issue #1563. With no record of the id a definition
    /// was published as, the anchor a starting action chains from had to be RECOMPUTED from
    /// <c>(registerId, blueprintId)</c> — a formula that grew four homes and, being version-blind,
    /// deduped every republish into one silently-dropped transaction.
    /// </para>
    /// </remarks>
    public string PublicationTxId { get; init; } = string.Empty;

    /// <summary>
    /// The executable-definition hash — the content address of this definition, and the value an
    /// instance is pinned to (Feature 194). Computed at publish over exactly the bytes stored here.
    /// </summary>
    /// <remarks>
    /// Two publications whose executable definitions are identical (a presentational-only
    /// republish) share this value, which is what stops a relabelled field stranding running
    /// instances on an older definition for no reason.
    /// </remarks>
    public string ExecDefHash { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; set; }
    public string? RegisterId { get; init; }
}

/// <summary>
/// Publish result
/// </summary>
public record PublishResult
{
    public bool IsSuccess { get; init; }
    public PublishedBlueprint? PublishedBlueprint { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];

    public static PublishResult Success(PublishedBlueprint published, string[]? warnings = null) => new()
    {
        IsSuccess = true,
        PublishedBlueprint = published,
        Warnings = warnings ?? []
    };

    public static PublishResult Failed(params string[] errors) => new()
    {
        IsSuccess = false,
        Errors = errors
    };
}

// ===========================
// Execution Endpoint Request DTOs (Sprint 5)
// ===========================

/// <summary>
/// Request for validating action data
/// </summary>
public record ValidateRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for applying calculations
/// </summary>
public record CalculateRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for determining routing
/// </summary>
public record RouteRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

/// <summary>
/// Request for applying disclosure rules
/// </summary>
public record DiscloseRequest
{
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required Dictionary<string, object> Data { get; init; }
}

// ===========================
// Notification DTOs (Sprint 5)
// ===========================

/// <summary>
/// Notification sent by Register Service when a transaction is confirmed
/// </summary>
public record TransactionConfirmationNotification
{
    public required string TransactionHash { get; init; }
    public required string WalletAddress { get; init; }
    public required string RegisterAddress { get; init; }
    public string? BlueprintId { get; init; }
    public string? ActionId { get; init; }
    public string? InstanceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

// ===========================
// Instance DTOs (Sprint 6)
// ===========================

/// <summary>
/// Request to create a new workflow instance
/// </summary>
public record CreateInstanceRequest
{
    /// <summary>
    /// The ID of the blueprint to instantiate
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The register ID where transactions will be stored
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Optional tenant ID for isolation (defaults to "default")
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Optional metadata to associate with the instance
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}
