// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Auth;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr (stdout is reserved for MCP communication)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Load configuration
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    // Unprefixed env vars so the platform-wide JwtSettings__InstallationName / __SigningKey
    // (set by docker-compose) are visible — the MCP server must validate against the same
    // installation identity + signing key as token issuance (spec 136).
    .AddEnvironmentVariables()
    .AddEnvironmentVariables("SORCHA_");

// Parse command-line arguments for JWT token
var jwtToken = GetJwtToken(args, builder.Configuration);
if (string.IsNullOrEmpty(jwtToken))
{
    Console.Error.WriteLine("Error: JWT token is required. Provide via --jwt-token argument or SORCHA_JWT_TOKEN environment variable.");
    return 1;
}

// Register configuration options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
// Spec 136: validate Tenant-issued tokens against the installation's single source of truth —
// no shared issuer default. Issuer + tier audiences derive from JwtSettings:InstallationName
// (the same value token issuance uses); a configured Jwt:Issuer still wins. The signing key
// falls back to the platform's shared key when the MCP-specific one is unset.
builder.Services.PostConfigure<JwtOptions>(o =>
{
    var installationName = builder.Configuration["JwtSettings:InstallationName"]
        ?? builder.Configuration["Jwt:InstallationName"];
    var explicitIssuer = string.IsNullOrWhiteSpace(o.Issuer) ? null : o.Issuer;
    o.Issuer = SorchaIssuer.Resolve(
        explicitIssuer, installationName, SorchaIssuer.AllowsDevLocalFallback(builder.Environment));
    o.Audiences = new SorchaAudiences(installationName).All.ToArray();
    if (string.IsNullOrEmpty(o.SigningKey))
    {
        o.SigningKey = builder.Configuration["JwtSettings:SigningKey"];
    }
});
builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection(RateLimitSettings.SectionName));

// Register JWT validation handler
builder.Services.AddSingleton<IJwtValidationHandler, JwtValidationHandler>();

// Register MCP session service - initialized with JWT token
builder.Services.AddSingleton<IMcpSessionService>(sp =>
{
    var jwtHandler = sp.GetRequiredService<IJwtValidationHandler>();
    var logger = sp.GetRequiredService<ILogger<McpSessionService>>();
    var session = new McpSessionService(jwtHandler, logger);
    session.InitializeFromToken(jwtToken);
    return session;
});

// Spec 139: the stdio session instance is also the ambient caller identity and the source
// of the bearer token forwarded to backends (one caller per process on stdio).
builder.Services.AddSingleton<ICallerContext>(sp => (ICallerContext)sp.GetRequiredService<IMcpSessionService>());

// Register MCP infrastructure services
builder.Services.AddSingleton<IMcpAuthorizationService, McpAuthorizationService>();
builder.Services.AddSingleton<IRateLimitService, RateLimitService>();
builder.Services.AddSingleton<IToolAuditService, ToolAuditService>();
builder.Services.AddSingleton<IMcpErrorHandler, McpErrorHandler>();
builder.Services.AddSingleton<IServiceAvailabilityTracker, ServiceAvailabilityTracker>();

// Register Sorcha service clients for backend communication
builder.Services.AddServiceClients(builder.Configuration);

// Spec 139: forward the caller's bearer to every backend call. Attaching the handler to the
// default HttpClient covers tools that resolve clients via IHttpClientFactory; the backend
// (API Gateway) then authorizes the operation as the calling identity rather than anonymously.
builder.Services.AddTransient<CallerTokenForwardingHandler>();
builder.Services.AddHttpClient(string.Empty).AddHttpMessageHandler<CallerTokenForwardingHandler>();

// Spec 139 US4: as tools are reconciled onto typed Sorcha.ServiceClients, attach the
// forwarding handler to each typed client's HttpClient (keyed by concrete type name) so the
// caller's bearer rides every typed call. Base addresses come from ServiceClients:*:Address
// (point these at the API Gateway in deployment config).
builder.Services.AddHttpClient<Sorcha.ServiceClients.Blueprint.BlueprintServiceClient>()
    .AddHttpMessageHandler<CallerTokenForwardingHandler>();

// Configure MCP server with stdio transport and auto-discovery
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "Sorcha MCP Server",
            Version = "1.0.0"
        };
        options.ServerInstructions = """
            Sorcha MCP Server - A Model Context Protocol server for the Sorcha decentralised register platform.

            Available tool categories based on your role:
            - Administrator (sorcha:admin): Platform health, logs, metrics, tenant/user management
            - Designer (sorcha:designer): Blueprint creation, validation, simulation, versioning
            - Participant (sorcha:participant): Inbox, actions, transactions, wallet operations

            Use the appropriate tools based on your assigned role.
            """;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    // Spec 139: narrow the advertised tools/list to the caller's tier/role entitlement so a
    // consumer never even sees admin/designer tools. Advisory only — invocation-time gating
    // (McpAuthorizationService) and the gateway remain the authoritative enforcement.
    .WithRequestFilters(filters =>
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);

            var authz = context.Services?.GetService<IMcpAuthorizationService>();
            if (authz is not null && result.Tools.Count > 0)
            {
                var allowed = authz.GetAuthorizedTools().ToHashSet(StringComparer.Ordinal);
                result.Tools = [.. result.Tools.Where(tool => allowed.Contains(tool.Name))];
            }

            return result;
        });
    });

var app = builder.Build();

// Log startup information
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var session = app.Services.GetRequiredService<IMcpSessionService>();
var authService = app.Services.GetRequiredService<IMcpAuthorizationService>();

logger.LogInformation("Starting Sorcha MCP Server for user {UserId} with roles: {Roles}",
    session.CurrentSession?.UserId ?? "unknown",
    string.Join(", ", session.CurrentSession?.Roles ?? []));

logger.LogInformation("Available tools for this session: {ToolCount} tools",
    authService.GetAuthorizedTools().Count);

await app.RunAsync();

return 0;

/// <summary>
/// Extracts JWT token from command-line arguments or environment variables.
/// </summary>
static string? GetJwtToken(string[] args, IConfiguration configuration)
{
    // First check command-line arguments
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--jwt-token")
        {
            return args[i + 1];
        }
    }

    // Then check environment variable
    return configuration["JWT_TOKEN"];
}
