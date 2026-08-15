// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentValidation;
using Grpc.Net.Client;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Endpoints;
using Sorcha.Validator.Service.Extensions;
using Sorcha.Validator.Service.Services;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Utilities;
using Sorcha.ServiceClients.Extensions;
using Sorcha.Register.Storage.MongoDB;
using Sorcha.Register.Storage.Redis;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Kestrel on plaintext HTTP can't multiplex HTTP/1.1 + HTTP/2 on one port
// (h2c needs ALPN, which needs TLS). Bind REST on 8080 and gRPC on a
// dedicated HTTP/2-only port for ValidatorGrpcService.
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

// Add JWT authentication (shared from ServiceDefaults)
builder.AddJwtAuthentication();

// Add authorization policies for Validator Service
builder.Services.AddValidatorAuthorization();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add Redis for distributed coordination and memory pool persistence
builder.AddRedisClient("redis");

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Validator Service API", "Transaction validation, consensus, chain integrity verification, and docket building for the Sorcha decentralised register platform.");

// Configure strongly-typed configuration sections
builder.Services.Configure<Sorcha.Validator.Service.Configuration.ValidatorConfiguration>(
    builder.Configuration.GetSection("Validator"));
builder.Services.Configure<Sorcha.Validator.Service.Configuration.ConsensusConfiguration>(
    builder.Configuration.GetSection("Consensus"));
builder.Services.Configure<Sorcha.Validator.Service.Configuration.MemPoolConfiguration>(
    builder.Configuration.GetSection("MemPool"));
builder.Services.Configure<Sorcha.Validator.Service.Configuration.DocketBuildConfiguration>(
    builder.Configuration.GetSection("DocketBuild"));

// Configure WalletConfiguration (T013)
var walletConfig = builder.Configuration.GetSection("WalletService").Get<WalletConfiguration>()
    ?? throw new InvalidOperationException("WalletService configuration is required");
builder.Services.AddSingleton(walletConfig);

// Add Cryptography services (required for hashing and signing operations)
builder.Services.AddScoped<IHashProvider, HashProvider>();
builder.Services.AddSingleton<ICryptoModule, CryptoModule>(); // T013: Register ICryptoModule
builder.Services.AddScoped<MerkleTree>();
builder.Services.AddScoped<DocketHasher>();

// Register read-only repository for GovernanceRosterService (used by RightsEnforcementService).
// Register Service owns read-write access; Validator reads the same MongoDB for governance roster reconstruction.
// Only IReadOnlyRegisterRepository is registered — IRegisterRepository is NOT resolvable here.
builder.Services.AddReadOnlyMongoRegisterStorage(builder.Configuration);

// Event infrastructure: Redis Streams for cross-service event consumption
builder.Services.AddRedisEventStreams(config =>
{
    config.ConsumerGroup = "validator-service";
});

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Add Core validation services
builder.Services.AddScoped<Sorcha.Validator.Core.Validators.ITransactionValidator,
    Sorcha.Validator.Core.Validators.TransactionValidator>();
builder.Services.AddScoped<Sorcha.Validator.Core.Validators.IDocketValidator,
    Sorcha.Validator.Core.Validators.DocketValidator>();
builder.Services.AddScoped<Sorcha.Validator.Core.Validators.IConsensusValidator,
    Sorcha.Validator.Core.Validators.ConsensusValidator>();

// Add memory pool manager
builder.Services.AddSingleton<Sorcha.Validator.Service.Services.IMemPoolManager,
    Sorcha.Validator.Service.Services.MemPoolManager>();

// Add register monitoring registry (singleton - shared state across services)
builder.Services.AddSingleton<Sorcha.Validator.Service.Services.IRegisterMonitoringRegistry,
    Sorcha.Validator.Service.Services.RegisterMonitoringRegistry>();

// Add system wallet provider (singleton - maintains wallet reference across service lifetime)
builder.Services.AddSingleton<Sorcha.Validator.Service.Services.ISystemWalletProvider,
    Sorcha.Validator.Service.Services.SystemWalletProvider>();

// Add consensus engine
builder.Services.AddScoped<Sorcha.Validator.Service.Services.IConsensusEngine,
    Sorcha.Validator.Service.Services.ConsensusEngine>();

// Add genesis manager (scoped to match service client lifetimes)
builder.Services.AddScoped<Sorcha.Validator.Service.Services.IGenesisManager,
    Sorcha.Validator.Service.Services.GenesisManager>();

// Add docket builder
builder.Services.AddScoped<Sorcha.Validator.Service.Services.IDocketBuilder,
    Sorcha.Validator.Service.Services.DocketBuilder>();

// Add validator orchestrator (scoped to match service client lifetimes)
builder.Services.AddScoped<Sorcha.Validator.Service.Services.IValidatorOrchestrator,
    Sorcha.Validator.Service.Services.ValidatorOrchestrator>();

// Add consolidated service clients
builder.Services.AddServiceClients(builder.Configuration);

// Add blueprint cache, fetcher, and transaction pool poller (required by validation engine)
builder.Services.AddBlueprintCache(builder.Configuration);
builder.Services.AddBlueprintFetcher(builder.Configuration);
builder.Services.AddTransactionPoolPoller(builder.Configuration);

// Add verified transaction queue (required by validation engine)
builder.Services.AddVerifiedTransactionQueue(builder.Configuration);

// Add validation engine (schema validation, chain validation)
builder.Services.AddValidationEngine(builder.Configuration);

// Add gated benchmark instrumentation. No-op when Validator:Benchmark:Enabled
// is false. See bench/baseline-2026-05/README.md for the capture procedure.
builder.Services.AddValidatorBenchmarking(builder.Configuration);

// Configure gRPC channel for Wallet Service (T014)
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<WalletConfiguration>();
    return GrpcChannel.ForAddress(config.Endpoint, new GrpcChannelOptions
    {
        // Configure HTTP/2 keep-alive for long-running connections
        HttpHandler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true
        }
    });
});

// Register WalletIntegrationService as singleton (T014)
// Singleton lifetime chosen for:
// - Wallet details cached for service lifetime (FR-002)
// - Derived key cache persists across requests (performance)
// - Thread-safe implementation with SemaphoreSlim
builder.Services.AddSingleton<IWalletIntegrationService, WalletIntegrationService>();

// Feature 108 — validator key resolution + roster-driven monitoring enrolment
builder.Services.AddSingleton<Sorcha.Validator.Service.Services.IValidatorKeyProvider,
    Sorcha.Validator.Service.Services.ValidatorKeyProvider>();

// Add background services
builder.Services.AddHostedService<Sorcha.Validator.Service.Services.SystemWalletInitializer>();
builder.Services.AddHostedService<Sorcha.Validator.Service.Services.MemPoolCleanupService>();
// Hydrate Redis from Mongo BEFORE DocketBuildTriggerService starts heartbeating,
// so a cold Redis (post `down -v`) doesn't cause re-registration loops.
builder.Services.AddHostedService<Sorcha.Validator.Service.Services.ValidatorRegistryHydrationService>();
builder.Services.AddHostedService<Sorcha.Validator.Service.Services.DocketBuildTriggerService>();

// Feature 108 — roster-driven monitoring bootstrap
builder.Services.AddHostedService<Sorcha.Validator.Service.Services.RegisterMonitoringBootstrap>();

// Add metrics support services (VAL-9.45)
// PendingDocketStore and ExceptionResponseHandler have no deep dependencies.
// ConsensusFailureHandler and DocketDistributor require the full consensus chain
// (SignatureCollector → LeaderElection → Peer gRPC) which is not wired in MVD.
// The metrics endpoint handles missing services gracefully with defaults.
builder.Services.AddPendingDocketStore();
builder.Services.AddExceptionResponseHandler();

// Add genesis configuration service (Sprint 9F)
builder.Services.AddGenesisConfigService(builder.Configuration);

// Add validator registry (Sprint 9F)
builder.Services.AddValidatorRegistry(builder.Configuration);

// Add control blueprint version resolver (Sprint 9F - VAL-9.42)
builder.Services.AddControlBlueprintVersionResolver();

// Add control docket processor (Sprint 9F - VAL-9.41)
builder.Services.AddControlDocketProcessor();

// Add gRPC services.
//
// The peer surface is deliberately NOT behind .RequireAuthorization(): validator-to-validator
// consensus is federated across installations, and Sorcha's service tokens are installation-scoped
// by design (Feature 136 rejects another installation's tokens), so a blanket gate would sever
// federation permanently rather than merely during a rolling deploy. Instead
// ValidatorGrpcAccessInterceptor applies the same opportunistic-auth shape as Peer's interceptor
// and then ENFORCES a per-method policy: the federation-necessary methods (whose payloads are
// signature-verified against the validator roster downstream) stay reachable, everything else —
// including anything newly added — requires an authenticated caller. See
// ValidatorGrpcAccessPolicy for the reasoning per method.
builder.Services.AddSingleton<Sorcha.Validator.Service.GrpcServices.ValidatorGrpcAccessInterceptor>();
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<Sorcha.Validator.Service.GrpcServices.ValidatorGrpcAccessInterceptor>();
});

var app = builder.Build();

// Issue #1433 — sanitized global exception handler, FIRST in the pipeline so it wraps every
// other middleware's unhandled exceptions too (see ServiceDefaults.Extensions for rationale).
app.UseSanitizedExceptionHandling();

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

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Map gRPC services
app.MapGrpcService<Sorcha.Validator.Service.GrpcServices.ValidatorGrpcService>();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Sorcha Validator Service API", theme: Scalar.AspNetCore.ScalarTheme.Mars);

// Map API endpoints (protected — requires authentication)
app.MapGroup("/api/v1/transactions")
    .WithTags("Validation")
    .RequireAuthorization("CanValidateChains")
    .MapValidationEndpoints();

// Map admin endpoints (protected — requires authentication)
app.MapAdminEndpoints();

// Map validator registration endpoints (protected — requires service token or admin)
app.MapGroup("/api/validators")
    .WithTags("Validators")
    .RequireAuthorization("RequireAuthenticated")
    .MapValidatorRegistrationEndpoints();

// Map BLS threshold signing endpoints (protected — requires service token)
app.MapGroup("/api/v1/validators/threshold")
    .WithTags("Threshold")
    .RequireAuthorization("RequireService")
    .MapThresholdEndpoints();

// Map metrics endpoints (protected — requires authentication)
app.MapMetricsEndpoints();

// Map benchmark/telemetry endpoints. The Validator:Benchmark:Enabled flag is
// the real security boundary — when disabled, every endpoint returns
// {enabled:false, message:"telemetry disabled"} regardless of caller. The
// auth gate is RequireAuthenticated (not RequireService) so the baseline
// capture harness can use any walkthrough-bootstrapped user JWT without
// needing a service-principal token to be minted separately.
app.MapGroup("")
    .RequireAuthorization("RequireAuthenticated")
    .MapBenchmarkEndpoints();

app.Run();
