// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Scalar.AspNetCore;
using Sorcha.ApiGateway.Models;
using Sorcha.ApiGateway.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Aspire service defaults
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Add YARP reverse proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add health aggregation service
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<HealthAggregationService>();

// Configure Tenant Service HTTP client for URL resolution
builder.Services.AddHttpClient("TenantService", client =>
{
    var tenantServiceUrl = builder.Configuration.GetValue<string>("Services:TenantService:BaseUrl") ?? "http://tenant-service";
    client.BaseAddress = new Uri(tenantServiceUrl);
});

// Add dashboard statistics service
builder.Services.AddSingleton<DashboardStatisticsService>();

// Add alert aggregation service
builder.Services.Configure<Sorcha.ApiGateway.Models.AlertThresholdConfig>(
    builder.Configuration.GetSection("AlertThresholds"));
builder.Services.AddSingleton<AlertAggregationService>();

// Add client download service
builder.Services.AddSingleton<ClientDownloadService>();

// Add OpenAPI aggregation service
builder.Services.AddSingleton<OpenApiAggregationService>();

// Add JWT authentication (AUTH-005)
builder.AddJwtAuthentication();

// Add shared authorization policies (AUTH-005)
builder.AddSorchaAuthorizationPolicies();

// Add conditional OpenAPI authorization policy
var requireOpenApiAuth = builder.Configuration.GetValue<bool>("OpenApi:RequireAuth", true);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OpenApiAccess", policy =>
    {
        if (requireOpenApiAuth)
            policy.RequireAuthenticatedUser();
        else
            policy.RequireAssertion(_ => true); // Allow anonymous
    });
});

// Add OpenAPI documentation
builder.Services.AddOpenApi();

// Add CORS for frontend - production restriction handled at infrastructure level
builder.AddSorchaCors();

var app = builder.Build();

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

// Enable CORS
app.UseCors();

// Enable authentication and authorization (AUTH-005)
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002) - applies to all proxied requests
app.UseRateLimiting();

// ===========================
// Aggregated Health Endpoint
// ===========================

app.MapGet("/api/health", async (HealthAggregationService healthService) =>
{
    var health = await healthService.GetAggregatedHealthAsync();
    var statusCode = health.Status switch
    {
        "healthy" => 200,
        "degraded" => 200,
        _ => 503
    };

    return Results.Json(health, statusCode: statusCode);
})
.WithName("AggregatedHealth")
.WithSummary("Get aggregated health status from all services")
.WithTags("Health");

// ===========================
// System Statistics Endpoint
// ===========================

app.MapGet("/api/stats", async (HealthAggregationService healthService) =>
{
    var stats = await healthService.GetSystemStatisticsAsync();
    return Results.Ok(stats);
})
.WithName("SystemStatistics")
.WithSummary("Get system-wide statistics from all services")
.WithTags("System");

// ===========================
// Dashboard Statistics Endpoint
// ===========================

app.MapGet("/api/dashboard", async (DashboardStatisticsService dashboardService) =>
{
    var stats = await dashboardService.GetDashboardStatisticsAsync();
    return Results.Ok(stats);
})
.WithName("DashboardStatistics")
.WithSummary("Get dashboard statistics from all backend services (blueprints, wallets, registers, etc.)")
.WithTags("Dashboard");

// ===========================
// Alerts Endpoint
// ===========================

app.MapGet("/api/alerts", async (AlertAggregationService alertService) =>
{
    var alerts = await alertService.GetAlertsAsync();
    return Results.Ok(alerts);
})
.WithName("Alerts")
.WithSummary("Get active alerts from service metric evaluation")
.WithTags("Monitoring");

// ===========================
// API Documentation Index
// ===========================

app.MapGet("/api/docs", (IConfiguration configuration) =>
{
    var services = new object[]
    {
        new { Name = "API Gateway", Description = "Gateway endpoints for health, stats, and client management",
              ScalarUrl = "/scalar/", OpenApiUrl = "/openapi/v1.json" },
        new { Name = "All Services (Aggregated)", Description = "Combined documentation from all backend services",
              ScalarUrl = "/scalar/", OpenApiUrl = "/openapi/aggregated.json" },
        new { Name = "Blueprint Service", Description = "Blueprint and workflow management APIs",
              OpenApiUrl = "/api/blueprint/openapi/v1.json" },
        new { Name = "Wallet Service", Description = "Cryptographic wallet and signing APIs",
              OpenApiUrl = "/api/wallet/openapi/v1.json" },
        new { Name = "Register Service", Description = "Distributed ledger and transaction APIs",
              OpenApiUrl = "/api/register/openapi/v1.json" },
        new { Name = "Tenant Service", Description = "Authentication and multi-tenant management APIs",
              OpenApiUrl = "/api/tenant/openapi/v1.json" },
        new { Name = "Peer Service", Description = "P2P networking and peer discovery APIs",
              OpenApiUrl = "/api/peer/openapi/v1.json" },
        new { Name = "Validator Service", Description = "Transaction validation and consensus APIs",
              OpenApiUrl = "/api/validator/openapi/v1.json" }
    };

    var aspireDashboardUrl = configuration["Dashboard:AspireDashboardUrl"] ?? "http://localhost:15888";
    var showAspireLink = configuration.GetValue<bool>("Dashboard:ShowAspireLink", true);

    return Results.Ok(new
    {
        Title = "Sorcha API Documentation Index",
        Description = "Links to OpenAPI documentation for all Sorcha platform services",
        AspireDashboard = showAspireLink ? new { Url = aspireDashboardUrl, Description = ".NET Aspire orchestration dashboard" } : null,
        Services = services
    });
})
.WithName("ApiDocsIndex")
.WithSummary("Get an index of all API documentation endpoints")
.WithTags("Documentation");

// ===========================
// Client Download Endpoints
// ===========================

app.MapGet("/api/client/info", (ClientDownloadService clientService) =>
{
    var info = clientService.GetClientInfo();
    return Results.Ok(info);
})
.WithName("ClientInfo")
.WithSummary("Get information about the Blazor client application")
.WithTags("Client");

app.MapGet("/api/client/download", async (ClientDownloadService clientService, IWebHostEnvironment env) =>
{
    try
    {
        // Path to the client project (relative to gateway)
        var clientPath = Path.Combine(env.ContentRootPath, "..", "..", "UI", "Sorcha.Blueprint.Designer.Client");
        var fullPath = Path.GetFullPath(clientPath);

        var packageBytes = await clientService.CreateClientPackageAsync(fullPath);

        return Results.File(
            packageBytes,
            "application/zip",
            $"sorcha-client-{DateTime.UtcNow:yyyyMMdd}.zip");
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.Problem(
            title: "Client Not Found",
            detail: ex.Message,
            statusCode: 404);
    }
})
.WithName("DownloadClient")
.WithSummary("Download the Blazor client application source code as a ZIP package")
.WithTags("Client");

app.MapGet("/api/client/instructions", (ClientDownloadService clientService) =>
{
    var instructions = clientService.GetInstallationInstructions();
    return Results.Text(instructions, "text/markdown");
})
.WithName("InstallationInstructions")
.WithSummary("Get installation instructions for the client application")
.WithTags("Client");

// ===========================
// Gateway Status Page (moved from / to /gateway to allow Admin UI to serve homepage)
// ===========================

app.MapGet("/gateway", async (HealthAggregationService healthService, DashboardStatisticsService dashboardService, IConfiguration configuration, HttpContext context) =>
{
    var stats = await healthService.GetSystemStatisticsAsync();
    var health = await healthService.GetAggregatedHealthAsync();
    var dashboard = await dashboardService.GetDashboardStatisticsAsync();
    var aspireDashboardUrl = configuration["Dashboard:AspireDashboardUrl"] ?? "http://localhost:15888";
    var showAspireLink = configuration.GetValue<bool>("Dashboard:ShowAspireLink", true);

    var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Sorcha API Gateway Status</title>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }
        .container {
            background: white;
            border-radius: 16px;
            box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
            max-width: 1200px;
            width: 100%;
            overflow: hidden;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }
        .header h1 {
            font-size: 2.5rem;
            margin-bottom: 10px;
        }
        .header p {
            font-size: 1.1rem;
            opacity: 0.9;
        }
        .content {
            padding: 40px;
        }
        .stats-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 20px;
            margin-bottom: 40px;
        }
        .stat-card {
            background: #f8f9fa;
            padding: 24px;
            border-radius: 12px;
            border-left: 4px solid #667eea;
        }
        .stat-card h3 {
            color: #667eea;
            font-size: 0.875rem;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 8px;
        }
        .stat-card .value {
            font-size: 2rem;
            font-weight: bold;
            color: #2d3748;
        }
        .services-section {
            margin-bottom: 40px;
        }
        .services-section h2 {
            color: #2d3748;
            margin-bottom: 20px;
            font-size: 1.5rem;
        }
        .service-list {
            display: grid;
            gap: 12px;
        }
        .service-item {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 16px 20px;
            background: #f8f9fa;
            border-radius: 8px;
            border-left: 4px solid #e2e8f0;
        }
        .service-item.healthy {
            border-left-color: #48bb78;
        }
        .service-item.unhealthy {
            border-left-color: #f56565;
        }
        .service-name {
            font-weight: 600;
            color: #2d3748;
            text-transform: capitalize;
        }
        .service-status {
            display: inline-block;
            padding: 4px 12px;
            border-radius: 12px;
            font-size: 0.875rem;
            font-weight: 600;
        }
        .service-status.healthy {
            background: #c6f6d5;
            color: #22543d;
        }
        .service-status.unhealthy {
            background: #fed7d7;
            color: #742a2a;
        }
        .actions {
            display: flex;
            gap: 16px;
            flex-wrap: wrap;
        }
        .btn {
            display: inline-block;
            padding: 12px 24px;
            border-radius: 8px;
            text-decoration: none;
            font-weight: 600;
            transition: all 0.2s;
        }
        .btn-primary {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }
        .btn-primary:hover {
            transform: translateY(-2px);
            box-shadow: 0 10px 20px rgba(102, 126, 234, 0.4);
        }
        .btn-secondary {
            background: white;
            color: #667eea;
            border: 2px solid #667eea;
        }
        .btn-secondary:hover {
            background: #667eea;
            color: white;
        }
        .footer {
            text-align: center;
            padding: 20px;
            color: #718096;
            font-size: 0.875rem;
            border-top: 1px solid #e2e8f0;
        }
        .timestamp {
            color: #718096;
            font-size: 0.875rem;
            margin-top: 20px;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🚀 Sorcha API Gateway</h1>
            <p>Unified API endpoint for Sorcha blockchain services</p>
        </div>

        <div class="content">
            <h2 style="color: #2d3748; margin-bottom: 20px; font-size: 1.5rem;">System Health</h2>
            <div class="stats-grid">
                <div class="stat-card">
                    <h3>Total Services</h3>
                    <div class="value">{{stats.TotalServices}}</div>
                </div>
                <div class="stat-card">
                    <h3>Healthy Services</h3>
                    <div class="value">{{stats.HealthyServices}}</div>
                </div>
                <div class="stat-card">
                    <h3>System Status</h3>
                    <div class="value" style="text-transform: capitalize;">{{health.Status}}</div>
                </div>
            </div>

            <h2 style="color: #2d3748; margin: 30px 0 20px 0; font-size: 1.5rem;">Platform Statistics</h2>
            <div class="stats-grid">
                <div class="stat-card">
                    <h3>📋 Blueprints</h3>
                    <div class="value">{{dashboard.TotalBlueprints}}</div>
                    <div style="font-size: 0.875rem; color: #718096; margin-top: 8px;">
                        {{dashboard.TotalBlueprintInstances}} instances ({{dashboard.ActiveBlueprintInstances}} active)
                    </div>
                </div>
                <div class="stat-card">
                    <h3>💰 Wallets</h3>
                    <div class="value">{{dashboard.TotalWallets}}</div>
                </div>
                <div class="stat-card">
                    <h3>📚 Registers</h3>
                    <div class="value">{{dashboard.TotalRegisters}}</div>
                    <div style="font-size: 0.875rem; color: #718096; margin-top: 8px;">
                        {{dashboard.TotalTransactions}} transactions
                    </div>
                </div>
                <div class="stat-card">
                    <h3>🏢 Tenants</h3>
                    <div class="value">{{dashboard.TotalTenants}}</div>
                </div>
                <div class="stat-card">
                    <h3>🔗 Connected Peers</h3>
                    <div class="value">{{dashboard.ConnectedPeers}}</div>
                    <div style="font-size: 0.875rem; color: #718096; margin-top: 8px;">
                        Healthy P2P connections
                    </div>
                </div>
            </div>

            <div class="services-section">
                <h2>Service Status</h2>
                <div class="service-list">
{{string.Join("\n", health.Services.Select(s => $@"                    <div class=""service-item {s.Value.Status}"">
                        <span class=""service-name"">{s.Key}</span>
                        <span class=""service-status {s.Value.Status}"">{s.Value.Status}</span>
                    </div>"))}}
                </div>
            </div>

            <div class="actions">
                <a href="/" class="btn btn-primary">🏠 Sorcha UI Home</a>
                {{(showAspireLink ? $@"<a href=""{aspireDashboardUrl}"" class=""btn btn-primary"" target=""_blank"">🎛️ Aspire Dashboard</a>" : "")}}
                <a href="/scalar/" class="btn btn-primary">📚 API Documentation</a>
                <a href="/api/docs" class="btn btn-primary">📑 API Docs Index</a>
                <a href="/api/client/download" class="btn btn-secondary">💾 Download Client</a>
                <a href="/api/dashboard" class="btn btn-secondary">📊 Dashboard JSON</a>
                <a href="/api/health" class="btn btn-secondary">🏥 Health Check</a>
                <a href="/api/stats" class="btn btn-secondary">📈 System Stats</a>
                <a href="/api/client/instructions" class="btn btn-secondary">📖 Installation Guide</a>
            </div>

            <div class="timestamp">
                Last updated: {{stats.Timestamp:yyyy-MM-dd HH:mm:ss}} UTC
            </div>
        </div>

        <div class="footer">
            <p>Sorcha Blockchain Platform &copy; 2025 | Licensed under MIT</p>
        </div>
    </div>
</body>
</html>
""";

    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync(html);
})
.ExcludeFromDescription();

// ===========================
// OpenAPI Documentation
// ===========================

// Gateway's own OpenAPI
app.MapOpenApi();

// Aggregated OpenAPI from all services
app.MapGet("/openapi/aggregated.json", async (OpenApiAggregationService openApiService) =>
{
    var aggregatedSpec = await openApiService.GetAggregatedOpenApiAsync();
    return Results.Json(aggregatedSpec);
})
.WithName("AggregatedOpenApi")
.WithSummary("Get aggregated OpenAPI documentation from all backend services")
.WithTags("Documentation")
.ExcludeFromDescription();

// Scalar UI for aggregated documentation
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Sorcha API Gateway - All Services")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithOpenApiRoutePattern("/openapi/aggregated.json");
});

// Scalar UI for gateway-only documentation
app.MapGet("/scalar/gateway", () =>
{
    return Results.Redirect("/scalar/v1");
})
.ExcludeFromDescription();

// URL resolution middleware — resolves org subdomain from path/subdomain/custom domain
app.UseMiddleware<UrlResolutionMiddleware>();

// Map YARP reverse proxy (must be last)
app.MapReverseProxy();

app.Run();
