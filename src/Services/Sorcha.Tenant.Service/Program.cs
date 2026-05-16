// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Sorcha.AddressLookup;
using Sorcha.AtomicCache.Extensions;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Hubs;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceDefaults.Hubs;
using Sorcha.ServiceDefaults.Storage;
using Sorcha.Tenant.Service.Extensions;
using Sorcha.Tenant.Service.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add .NET Aspire service defaults (includes service discovery, health checks, and default observability)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Tenant Service API", "Multi-tenant organization management and authentication/authorization for the Sorcha decentralised register platform, including user management, service principal authentication, and role-based access control.");

// Add controllers and minimal API support
builder.Services.AddEndpointsApiExplorer();

// Configure JSON serialization with kebab-case enum handling for WebAuthn spec compliance.
// WebAuthn uses "public-key" (kebab-case) for PublicKeyCredentialType, which KebabCaseLower
// handles for both serialization and deserialization.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
});

// Add CORS - production restriction handled at API Gateway (YARP)
builder.AddSorchaCors();

// Add consolidated service clients (Wallet, Register, Blueprint, etc.)
builder.Services.AddServiceClients(builder.Configuration);

// Add Tenant Service dependencies (database, repositories, Redis, token revocation)
builder.Services.AddTenantServices(builder.Configuration);

// Add JWT authentication (shared across all services with auto-key generation)
builder.AddJwtAuthentication();

// Configure JwtConfiguration for token issuance (used by TokenService)
builder.Services.ConfigureJwtForTokenIssuance(builder.Configuration);

// Add authorization policies
builder.Services.AddTenantAuthorization();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add rate limiting (shared policies from ServiceDefaults, configured via RateLimiting appsettings section)
builder.AddRateLimiting();

// Configure ForwardedHeaders for Docker/reverse proxy support
// The API Gateway terminates TLS and forwards HTTP internally — the tenant service
// must trust X-Forwarded-Proto so antiforgery sees the original HTTPS request.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add Razor Pages for server-rendered auth UI
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Sorcha.Auth.AF";
    options.Cookie.Path = "/auth";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Add health checks (PostgreSQL and Redis when configured)
builder.Services.AddTenantHealthChecks(builder.Configuration);

// Feature 118 — TenantHub for identity / membership / admin / inbox realtime events (US2).
// Wires JWT auth + Redis backplane (ChannelPrefix=sorcha:signalr:tenant) +
// reconnect-with-jitter + OpenTelemetry instrumentation. Inbox event methods
// on ITenantHubClient land in Phase 5 (US3); the hub class is auth-only here
// so US3 has the surface to emit on.
builder.Services.AddSorchaHub<TenantHub, ITenantHubClient>(
    builder.Configuration, "/hubs/tenant", "tenant");

// Feature 118 / US3 — durable user inbox.
// Storage layer is registered through IStorageRegistrationLog (Feature 113 audit
// pattern). Tenant Service runs against Postgres in Production / Staging; the
// audited interface (Sorcha.Tenant.Service.Storage.IInboxStore) is recorded as
// persistent so storage-providers health and fail-fast enforcement apply.
{
    var storageLog = builder.Services.GetStorageRegistrationLog();
    builder.Services.AddScoped<IInboxStore, EfCoreInboxStore>();
    storageLog.RegisterPersistent(
        typeof(IInboxStore).FullName!,
        typeof(EfCoreInboxStore).FullName!,
        backend: "postgres");
}
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.IInboxService,
    Sorcha.Tenant.Service.Services.InboxService>();

// Add database initializer for automatic migration and seeding
// Creates default organization (sorcha.local) and admin user on startup
builder.Services.AddDatabaseInitializer();

// Add audit cleanup background service (daily purge of expired audit entries)
builder.Services.AddAuditCleanup();

// Feature 116: account-linking + auth-method management
// (challenge primitive, last-method floor, telemetry, daily token cleanup)
builder.Services.AddTenantAccountManagement();

// Add activity event services (event log and cleanup)
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.Interfaces.IEventService,
    Sorcha.Tenant.Service.Services.EventService>();
builder.Services.AddHostedService<Sorcha.Tenant.Service.Services.EventCleanupService>();

// Feature 096: X.509 Organisation Trust — trust anchor provisioning and org cert enrolment
builder.Services.AddSingleton<Sorcha.Tenant.Service.Trust.ITrustProvider,
    Sorcha.Tenant.Service.Trust.InternalCaTrustProvider>();

// Feature 092: Consumer persona — orchestrator + typed HttpClient to Wallet Service
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.IPersonaService,
    Sorcha.Tenant.Service.Services.PersonaService>();
builder.Services.AddHttpClient<Sorcha.Tenant.Service.Services.IPersonaCryptoClient,
    Sorcha.Tenant.Service.Services.PersonaCryptoClient>(client =>
{
    // Wallet Service base URL — resolved from configuration via Aspire service
    // discovery. In Docker Compose this is "http://wallet"; in Aspire/dev it
    // resolves via the service discovery address.
    var walletBase = builder.Configuration["ServiceClients:WalletService:Address"]
        ?? builder.Configuration["Services:wallet:http:0"]
        ?? "http://wallet";
    client.BaseAddress = new Uri(walletBase);
});

// Feature 126: enrolment-session service + atomic single-use JTI registry.
// AddAtomicDistributedCache is idempotent across services (Feature 113 primitive).
builder.Services.AddAtomicDistributedCache(builder.Configuration, "Tenant");
builder.Services.AddSingleton<EnrolSessionMetrics>();
builder.Services.AddScoped<IEnrolSessionService, EnrolSessionService>();

// Feature 128: 6-digit pairing short codes wrapping standalone enrol-session
// tokens. Used by the wallet PWA pairing-takeover sub-affordance and the
// mobile-web install fallback path.
builder.Services.AddScoped<IPairingShortCodeService, PairingShortCodeService>();

// Feature 128 US2: "Email me a link" pairing-resumption token service.
// 24-hour TTL, single-use via IAtomicDistributedCache (NonceStore pattern).
builder.Services.AddScoped<IPairingResumptionTokenService, PairingResumptionTokenService>();

builder.Services.Configure<ReturnToAllowlistOptions>(
    builder.Configuration.GetSection(ReturnToAllowlistOptions.SectionName));

// Feature 103 US3: address lookup service + providers. postcodes.io is
// registered as the default-on provider (no key, no rate limit, MIT). OS
// Places is conditionally registered when Tenant:AddressLookup:OsPlaces:ApiKey
// is configured.
builder.Services.AddSorchaAddressLookup(builder.Configuration);

var app = builder.Build();

// Run database migrations and seeding BEFORE app.Run() to prevent race conditions
// with background services (AuditCleanupService, EventCleanupService, etc.)
// that query the database immediately on startup.
// This matches the pattern used by Wallet and Blueprint services.
{
    var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();

    // Signal ready immediately — background services that await DatabaseReadySignal
    // will unblock, and the DB is guaranteed to be initialised at this point.
    var readySignal = app.Services.GetRequiredService<DatabaseReadySignal>();
    readySignal.Signal();
}

// Map default endpoints (OpenAPI, health checks)
app.MapDefaultEndpoints();

// Trust forwarded headers from API Gateway (must be before any middleware that checks scheme)
app.UseForwardedHeaders();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Configure the HTTP request pipeline
// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Sorcha Tenant Service API");

app.UseCors();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.UseStaticFiles();

// Map API endpoint groups
app.MapBootstrapEndpoints();
app.MapOrganizationEndpoints();
app.MapParticipantEndpoints();
app.MapAuthEndpoints();
app.MapPasskeyEndpoints();
app.MapAuthChallengeEndpoints();
app.MapAuthMethodsEndpoints();
app.MapPasswordEndpoints();
app.MapPublicPasskeyEndpoints();
app.MapServiceAuthEndpoints();
app.MapUserPreferenceEndpoints();
app.MapPlatformUserDeviceEndpoints();
app.MapPersonaEndpoints();
app.MapAddressLookupEndpoints();
app.MapOrgDidDocumentEndpoints();
app.MapTotpEndpoints();
app.MapIdpConfigurationEndpoints();
app.MapOidcEndpoints();
app.MapSocialLoginEndpoints();
app.MapPlatformSettingsEndpoints();
app.MapPlatformOrgEndpoints();
app.MapPlatformManagementEndpoints();
app.MapOrgSettingsEndpoints();
app.MapDomainRestrictionEndpoints();
app.MapAuditEndpoints();
app.MapInvitationEndpoints();
app.MapDashboardEndpoints();
app.MapCustomDomainEndpoints();
app.MapInternalEndpoints();
app.MapPushSubscriptionEndpoints();
app.MapEventEndpoints();
app.MapRegisterSubscriptionEndpoints();
app.MapRegisterInvitationEndpoints();
app.MapTrustEndpoints();
app.MapEnrolSessionEndpoints();
app.MapPairingShortCodeEndpoints();
app.MapPairingResumptionEndpoints();
app.MapRazorPages();

// Feature 118 — map TenantHub at /hubs/tenant (US2). Routed via API Gateway.
app.MapSorchaHubs();

// Feature 118 / US3 — durable user inbox endpoints.
app.MapMeInboxEndpoints();
app.MapInternalInboxEndpoints();

// Health check is provided by MapDefaultEndpoints() which maps /health and /alive
// The standard Aspire health endpoint returns plain text "Healthy" or "Unhealthy"

app.Run();
