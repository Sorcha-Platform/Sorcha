// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.ServiceClients.Extensions;
using Sorcha.Tenant.Service.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add .NET Aspire service defaults (includes service discovery, health checks, and default observability)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Tenant Service API", "Multi-tenant organization management and authentication/authorization for the Sorcha distributed ledger platform, including user management, service principal authentication, and role-based access control.");

// Add controllers and minimal API support
builder.Services.AddEndpointsApiExplorer();

// Configure JSON serialization to use string enum values
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    var rateLimitingEnabled = builder.Configuration.GetValue<bool>("RateLimiting:EnableRateLimiting");
    if (rateLimitingEnabled)
    {
        var permitLimit = builder.Configuration.GetValue<int>("RateLimiting:PermitLimit");
        var window = builder.Configuration.GetValue<int>("RateLimiting:Window");

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(window),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = builder.Configuration.GetValue<int>("RateLimiting:QueueLimit")
                }));
    }

    // TOTP validation rate limiting: 5 attempts per minute per IP
    options.AddFixedWindowLimiter(TotpEndpoints.TotpRateLimitPolicy, config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });

    // Public auth rate limiting: 5 attempts per minute per IP (passkey + social endpoints)
    options.AddFixedWindowLimiter(PublicAuthEndpoints.PublicAuthRateLimitPolicy, config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
});

// Add Razor Pages for server-rendered auth UI
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Sorcha.Auth.AF";
    options.Cookie.Path = "/auth";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Add health checks (PostgreSQL and Redis when configured)
builder.Services.AddTenantHealthChecks(builder.Configuration);

// Add database initializer for automatic migration and seeding
// Creates default organization (sorcha.local) and admin user on startup
builder.Services.AddDatabaseInitializer();

// Add audit cleanup background service (daily purge of expired audit entries)
builder.Services.AddAuditCleanup();

// Add activity event services (event log and cleanup)
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.Interfaces.IEventService,
    Sorcha.Tenant.Service.Services.EventService>();
builder.Services.AddHostedService<Sorcha.Tenant.Service.Services.EventCleanupService>();

var app = builder.Build();

// Map default endpoints (OpenAPI, health checks)
app.MapDefaultEndpoints();

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
app.MapPublicAuthEndpoints();
app.MapServiceAuthEndpoints();
app.MapUserPreferenceEndpoints();
app.MapTotpEndpoints();
app.MapIdpConfigurationEndpoints();
app.MapOidcEndpoints();
app.MapOrgSettingsEndpoints();
app.MapDomainRestrictionEndpoints();
app.MapAuditEndpoints();
app.MapInvitationEndpoints();
app.MapDashboardEndpoints();
app.MapCustomDomainEndpoints();
app.MapInternalEndpoints();
app.MapPushSubscriptionEndpoints();
app.MapEventEndpoints();
app.MapRazorPages();

// Health check is provided by MapDefaultEndpoints() which maps /health and /alive
// The standard Aspire health endpoint returns plain text "Healthy" or "Unhealthy"

app.Run();
