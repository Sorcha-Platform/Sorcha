// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Auth;

// Spec 139 US3: the MCP server serves two transports selected at startup via --transport.
//   stdio (default) — one caller per process; identity is the startup --jwt-token.
//   http            — Streamable HTTP; identity is the per-request Authorization bearer,
//                     validated by ASP.NET Core JWT bearer before dispatch.
var transport = GetTransport(args);

return transport == TransportMode.Http
    ? await RunHttpAsync(args)
    : await RunStdioAsync(args);

// ---------------------------------------------------------------------------
// stdio transport — preserved verbatim from the pre-US3 foundation.
// ---------------------------------------------------------------------------
static async Task<int> RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Configure logging to stderr (stdout is reserved for MCP communication)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    ConfigureConfiguration(builder.Configuration);

    // Parse command-line arguments for JWT token
    var jwtToken = GetJwtToken(args, builder.Configuration);
    if (string.IsNullOrEmpty(jwtToken))
    {
        Console.Error.WriteLine("Error: JWT token is required. Provide via --jwt-token argument or SORCHA_JWT_TOKEN environment variable.");
        return 1;
    }

    ConfigureJwtOptions(builder.Services, builder.Configuration, builder.Environment);
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

    RegisterMcpInfrastructure(builder.Services);
    RegisterServiceClients(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(ConfigureServerOptions)
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithAuthorizationNarrowingListToolsFilter();

    var app = builder.Build();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    var session = app.Services.GetRequiredService<IMcpSessionService>();
    var authService = app.Services.GetRequiredService<IMcpAuthorizationService>();

    logger.LogInformation("Starting Sorcha MCP Server (stdio) for user {UserId} with roles: {Roles}",
        session.CurrentSession?.UserId ?? "unknown",
        string.Join(", ", session.CurrentSession?.Roles ?? []));

    logger.LogInformation("Available tools for this session: {ToolCount} tools",
        authService.GetAuthorizedTools().Count);

    await app.RunAsync();

    return 0;
}

// ---------------------------------------------------------------------------
// Streamable HTTP transport (spec 139 US3).
// ---------------------------------------------------------------------------
static async Task<int> RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureConfiguration(builder.Configuration);

    // Spec 139 US3: validate the per-request bearer against the installation's issuer + tier
    // audiences (F136), so an absent/invalid/wrong-installation token is rejected before
    // dispatch. AddJwtAuthentication derives issuer + audiences from JwtSettings:InstallationName
    // — the same single source of truth token issuance uses.
    builder.AddJwtAuthentication();
    builder.Services.AddAuthorization();

    // Per-request caller identity from the validated HttpContext (token + claims).
    builder.Services.AddHttpContextAccessor();

    // JwtOptions is still configured for the local advisory tier-resolution path
    // (TierResolution over the validated principal mirrors the stdio derivation).
    ConfigureJwtOptions(builder.Services, builder.Configuration, builder.Environment);
    builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection(RateLimitSettings.SectionName));
    builder.Services.AddSingleton<IJwtValidationHandler, JwtValidationHandler>();

    // The HTTP caller context reads IHttpContextAccessor on every access, so a singleton
    // registration yields per-request values without making the forwarding handler capture a
    // scoped dependency (captive-dependency / cross-request token-bleed hazard).
    builder.Services.AddSingleton<ICallerContext, HttpCallerContext>();

    RegisterMcpInfrastructure(builder.Services);
    RegisterServiceClients(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(ConfigureServerOptions)
        // Stateless for horizontal scale; the per-request ICallerContext makes the advisory
        // tools/list filter and token forwarding work per-request automatically.
        .WithHttpTransport(o => o.Stateless = true)
        .WithToolsFromAssembly()
        .WithAuthorizationNarrowingListToolsFilter();

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();

    // The MCP HTTP endpoint is a protected resource: an absent/invalid bearer is rejected by
    // the auth middleware before the MCP handler dispatches anything.
    app.MapMcp().RequireAuthorization();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting Sorcha MCP Server (Streamable HTTP) — endpoint protected by JWT bearer.");

    await app.RunAsync();

    return 0;
}

// ---------------------------------------------------------------------------
// Shared wiring used by both transports.
// ---------------------------------------------------------------------------

static void ConfigureConfiguration(IConfigurationBuilder configuration)
{
    configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        // Unprefixed env vars so the platform-wide JwtSettings__InstallationName / __SigningKey
        // (set by docker-compose) are visible — the MCP server must validate against the same
        // installation identity + signing key as token issuance (spec 136).
        .AddEnvironmentVariables()
        .AddEnvironmentVariables("SORCHA_");
}

static void ConfigureJwtOptions(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
{
    services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
    // Spec 136: validate Tenant-issued tokens against the installation's single source of truth —
    // no shared issuer default. Issuer + tier audiences derive from JwtSettings:InstallationName
    // (the same value token issuance uses); a configured Jwt:Issuer still wins. The signing key
    // falls back to the platform's shared key when the MCP-specific one is unset.
    services.PostConfigure<JwtOptions>(o =>
    {
        var installationName = configuration["JwtSettings:InstallationName"]
            ?? configuration["Jwt:InstallationName"];
        var explicitIssuer = string.IsNullOrWhiteSpace(o.Issuer) ? null : o.Issuer;
        o.Issuer = SorchaIssuer.Resolve(
            explicitIssuer, installationName, SorchaIssuer.AllowsDevLocalFallback(environment));
        o.Audiences = new SorchaAudiences(installationName).All.ToArray();
        if (string.IsNullOrEmpty(o.SigningKey))
        {
            o.SigningKey = configuration["JwtSettings:SigningKey"];
        }
    });
}

static void RegisterMcpInfrastructure(IServiceCollection services)
{
    services.AddSingleton<IMcpAuthorizationService, McpAuthorizationService>();
    services.AddSingleton<IRateLimitService, RateLimitService>();
    services.AddSingleton<IToolAuditService, ToolAuditService>();
    services.AddSingleton<IMcpErrorHandler, McpErrorHandler>();
    services.AddSingleton<IServiceAvailabilityTracker, ServiceAvailabilityTracker>();
}

static void RegisterServiceClients(IServiceCollection services, IConfiguration configuration)
{
    // Register Sorcha service clients for backend communication
    services.AddServiceClients(configuration);

    // Spec 139: forward the caller's bearer to every backend call. Attaching the handler to the
    // default HttpClient covers tools that resolve clients via IHttpClientFactory; the backend
    // (API Gateway) then authorizes the operation as the calling identity rather than anonymously.
    services.AddTransient<CallerTokenForwardingHandler>();
    services.AddHttpClient(string.Empty).AddHttpMessageHandler<CallerTokenForwardingHandler>();

    // Spec 139 US4: as tools are reconciled onto typed Sorcha.ServiceClients, attach the
    // forwarding handler to each typed client's HttpClient (keyed by concrete type name) so the
    // caller's bearer rides every typed call. Base addresses come from ServiceClients:*:Address
    // (point these at the API Gateway in deployment config).
    services.AddHttpClient<Sorcha.ServiceClients.Blueprint.BlueprintServiceClient>()
        .AddHttpMessageHandler<CallerTokenForwardingHandler>();
    services.AddHttpClient<Sorcha.ServiceClients.Register.RegisterServiceClient>()
        .AddHttpMessageHandler<CallerTokenForwardingHandler>();
    services.AddHttpClient<Sorcha.ServiceClients.Wallet.WalletServiceClient>()
        .AddHttpMessageHandler<CallerTokenForwardingHandler>();
    services.AddHttpClient<Sorcha.ServiceClients.Tenant.TenantServiceClient>()
        .AddHttpMessageHandler<CallerTokenForwardingHandler>();
}

static void ConfigureServerOptions(McpServerOptions options)
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
}

static TransportMode GetTransport(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(args[i + 1], "http", StringComparison.OrdinalIgnoreCase)
                ? TransportMode.Http
                : TransportMode.Stdio;
        }
    }

    return TransportMode.Stdio;
}

/// <summary>
/// Extracts JWT token from command-line arguments or environment variables (stdio transport).
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
