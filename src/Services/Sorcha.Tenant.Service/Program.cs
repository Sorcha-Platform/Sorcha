// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Sorcha.AddressLookup;
using Sorcha.AtomicCache.Extensions;
using Sorcha.ServiceClients.Configuration;
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

// F191 (#1420): additive workload mTLS listener for certificate-credential service auth.
// No-op unless ServiceAuth:Mtls:ServerCertificate + ServiceAuth:Mtls:TrustBundle are configured;
// fail-fast at startup when configured material is unreadable.
builder.AddWorkloadMtlsListener();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Tenant Service API", "Multi-tenant organization management and authentication/authorization for the Sorcha decentralised register platform, including user management, service principal authentication, and role-based access control.");

// Add controllers and minimal API support
builder.Services.AddEndpointsApiExplorer();

// JSON serialization uses the single shared source of truth (Sorcha.Serialization.SorchaJson): camelCase
// properties + kebab-case string enums (WebAuthn needs "public-key"). Applied here rather than platform-
// wide because other services' responses feed standards surfaces (OAuth/OIDC snake_case, VC tokens) where
// a uniform casing would be wrong; the clients read both formats, so central control stays here.
builder.Services.ConfigureHttpJsonOptions(
    options => Sorcha.Serialization.SorchaJson.Configure(options.SerializerOptions));

// Add CORS - production restriction handled at API Gateway (YARP)
builder.AddSorchaCors();

// Add consolidated service clients (Wallet, Register, Blueprint, etc.)
builder.Services.AddServiceClients(builder.Configuration);

// Add Tenant Service dependencies (database, repositories, Redis, token revocation)
builder.Services.AddTenantServices(builder.Configuration);

// Add JWT authentication (shared across all services with auto-key generation)
builder.AddJwtAuthentication();

// Configure JwtConfiguration for token issuance (used by TokenService)
builder.Services.ConfigureJwtForTokenIssuance(
    builder.Configuration,
    Sorcha.ServiceDefaults.Auth.SorchaIssuer.AllowsDevLocalFallback(builder.Environment));

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

// Feature 096: X.509 Organisation Trust — trust anchor provisioning and org cert enrolment
builder.Services.AddSingleton<Sorcha.Tenant.Service.Trust.ITrustProvider,
    Sorcha.Tenant.Service.Trust.InternalCaTrustProvider>();

// Feature 135 (US2) — operator trust-list snapshots managed via the /api/v1/trust/trustlists admin
// surface. In-memory, operator-pushed (re-pushable on restart), consulted by the `trustlist` trust
// source; a live LOTL provider is a future impl behind the same ITrustListProvider seam.
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.OperatorSnapshotTrustListProvider>();
builder.Services.AddSingleton<Sorcha.ServiceClients.Trust.ITrustListProvider>(sp =>
    sp.GetRequiredService<Sorcha.ServiceClients.Trust.OperatorSnapshotTrustListProvider>());

// Feature 181 US3 — authoritative EF-backed store for imported ETSI TS 119 612 trusted-list
// snapshots. Warn-tier (trust config, reloadable — NOT on the fail-fast audited list, mirroring the
// F114 presentation-store precedent); recorded via the storage log per pattern #10.
{
    var storageLog = builder.Services.GetStorageRegistrationLog();
    builder.Services.AddScoped<Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore,
        Sorcha.Tenant.Service.Storage.EfTrustedListSnapshotStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Tenant.Service.Storage.ITrustedListSnapshotStore).FullName!,
        typeof(Sorcha.Tenant.Service.Storage.EfTrustedListSnapshotStore).FullName!,
        backend: "postgres");
}
builder.Services.AddScoped<Sorcha.Tenant.Service.Trust.TrustedListImportService>();
// Fetch-once client for trusted-list URL import (operator-attested; admin-only endpoint).
builder.Services.AddHttpClient("trustlist-fetch", c => c.Timeout = TimeSpan.FromSeconds(30));

// Feature 181 US4 — durable certificate persistence (tenant roots, org certs, CSR audit) so issued-
// credential x5c chains survive restart (research R8). Warn-tier (trust config; the write-through
// InternalCaTrustProvider caches it) — NOT on the fail-fast audited list.
{
    var storageLog = builder.Services.GetStorageRegistrationLog();
    builder.Services.AddScoped<Sorcha.Tenant.Service.Storage.ICertificateStore,
        Sorcha.Tenant.Service.Storage.EfCertificateStore>();
    storageLog.RegisterPersistent(
        typeof(Sorcha.Tenant.Service.Storage.ICertificateStore).FullName!,
        typeof(Sorcha.Tenant.Service.Storage.EfCertificateStore).FullName!,
        backend: "postgres");
}
// Feature 181 US4 — org-certificate lifecycle (eligibility, CSR, import validation, chain resolution).
builder.Services.AddScoped<Sorcha.Tenant.Service.Trust.IOrgCertificateService,
    Sorcha.Tenant.Service.Trust.OrgCertificateService>();

// Feature 092: Consumer persona — orchestrator + typed HttpClient to Wallet Service
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.IPersonaService,
    Sorcha.Tenant.Service.Services.PersonaService>();
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.IPersonaInboxWriter,
    Sorcha.Tenant.Service.Services.PersonaInboxWriter>();
builder.Services.AddHttpClient<Sorcha.Tenant.Service.Services.IPersonaCryptoClient,
    Sorcha.Tenant.Service.Services.PersonaCryptoClient>(client =>
{
    // Wallet Service base URL — resolved from configuration via Aspire service
    // discovery. In Docker Compose this is "http://wallet"; in Aspire/dev it
    // resolves via the service discovery address.
    var walletBase = SorchaServiceAddresses.TryResolve(builder.Configuration, SorchaService.Wallet)
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

// Issue #1433 — sanitized global exception handler, FIRST in the pipeline so it wraps every
// other middleware's unhandled exceptions too (see ServiceDefaults.Extensions for rationale).
app.UseSanitizedExceptionHandling();

// F191 US3 (#1420): make a retired-secrets deployment diagnosable from startup logs — with the
// flag on, every secret-presenting service-auth request is refused platform-wide.
if (app.Configuration.GetValue<bool>(Sorcha.WorkloadIdentity.WorkloadIdentityConfig.DisableSharedSecrets))
{
    app.Logger.LogWarning(
        "ServiceAuth:DisableSharedSecrets is ENABLED — shared-secret service authentication is disabled; " +
        "only workload-certificate credentials mint service tokens (F191/#1420)");
}

// Feature 146 — fail closed at startup if no at-rest secret-protection key can be resolved
// (resolves the singleton, which runs TenantSecretKeyResolver.ResolveProtectionKey()).
_ = app.Services.GetRequiredService<Sorcha.Tenant.Service.Services.ISecretProtectionProvider>();

// Run database migrations and seeding BEFORE app.Run() to prevent race conditions
// with background services (AuditCleanupService, etc.)
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
app.MapTwoFactorChannelEndpoints();
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
app.MapSocialLinkStepUpEndpoints();
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
