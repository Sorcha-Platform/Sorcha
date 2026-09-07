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

    McpServerHttpRegistration.RegisterMcpInfrastructure(builder.Services, builder.Configuration);
    McpServerHttpRegistration.RegisterServiceClients(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(ConfigureServerOptions)
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithResourcesFromAssembly()
        .WithPromptsFromAssembly()
        .WithAuthorizationNarrowingListToolsFilter()
        .WithToolInvocationAuditFilter()
        .WithArgumentBindingErrorFilter();

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

    // JwtOptions is still configured for the local advisory tier-resolution path
    // (TierResolution over the validated principal mirrors the stdio derivation).
    ConfigureJwtOptions(builder.Services, builder.Configuration, builder.Environment);
    builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection(RateLimitSettings.SectionName));

    // The HTTP caller context reads IHttpContextAccessor on every access, so a singleton
    // registration yields per-request values without making the forwarding handler capture a
    // scoped dependency (captive-dependency / cross-request token-bleed hazard). This is the
    // exact set of registrations the HTTP transport uses — extracted to McpServerHttpRegistration
    // (including the shared MCP infrastructure) so a test can build the same container the
    // server builds (MCP-P0).
    McpServerHttpRegistration.ConfigureServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(ConfigureServerOptions)
        // Stateless for horizontal scale; the per-request ICallerContext makes the advisory
        // tools/list filter and token forwarding work per-request automatically.
        .WithHttpTransport(o => o.Stateless = true)
        .WithToolsFromAssembly()
        .WithResourcesFromAssembly()
        .WithPromptsFromAssembly()
        .WithAuthorizationNarrowingListToolsFilter()
        .WithToolInvocationAuditFilter()
        .WithArgumentBindingErrorFilter();

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

static void ConfigureServerOptions(McpServerOptions options)
{
    options.ServerInfo = new()
    {
        Name = "Sorcha MCP Server",
        // MCP audit 2026-07-26: was hardcoded "1.0.0", so a client's initialize disagreed with the
        // manifest's derived version (§14). One source now — see McpServerVersion.
        Version = Sorcha.McpServer.Infrastructure.McpServerVersion.Current
    };
    // Task 8 (2026-09-07): this used to spend eight lines restating role names, one of which
    // (sorcha:participant) gates zero tools — the code can no longer produce the denial message
    // that names it. Replaced with a map of the lifecycle and the resources/prompts Task 7 and
    // this task added, so an agent's FIRST read of the server does not undersell what
    // sorcha://schema/blueprint covers (Ruling 2: it must agree with that resource's own
    // [Description] in Resources/SorchaResources.cs and Resources/LiveStateResources.cs, not
    // restate a weaker or stronger claim) or oversell what the human-approval gate guarantees
    // (Ruling 5: three refusal states, not two — see SorchaPrompts.HumanGateReminder).
    options.ServerInstructions = """
        Sorcha MCP Server — a decentralised register platform for multi-party data flow with
        cryptographically enforced selective disclosure.

        THE LIFECYCLE, IN ORDER. Most tasks follow it end to end:
          1. sorcha_blueprint_create   — define the workflow (participants, actions, disclosure)
          2. sorcha_register_create    — create the ledger it runs on          [needs a human]
          3. sorcha_blueprint_publish  — publish the definition to that register [may need a human]
          4. sorcha_instance_create    — start a running instance
          5. sorcha_action_submit      — perform an action on that instance

        READ THESE FIRST — they are resources, not tool calls, so they cost you nothing:
          sorcha://schema/blueprint   a JSON Schema for a blueprint. Everything it documents
                                      (participants, actions, data schemas, disclosure groups,
                                      action-level condition routing) is accurate and current —
                                      but it is INCOMPLETE, not wrong: it does not yet define
                                      `routes`, `isStartingAction`, `credentialRequirements`,
                                      `credentialIssuanceConfig`, `rejectionConfig`,
                                      `requiredPriorActions`, or `instanceReference`. Read the
                                      examples below for those constructs; do not treat the
                                      schema's silence on them as meaning they don't exist.
          sorcha://examples/{name}    working blueprints that use the constructs above:
                                      assured-identity, encryption-at-rest, ping-pong.
          sorcha://glossary           what register, docket, disclosure group and the rest mean.
          sorcha://registers          the registers you can see right now — capped at 50, with
                                      `count` and `truncated`. When `truncated` is true, a
                                      register's absence from this list is NOT evidence it
                                      doesn't exist.
          sorcha://instances          the workflow instances you can see right now.

        GUIDED RECIPES are available as prompts: sorcha_two_party_exchange,
        sorcha_issue_credential, sorcha_prove_to_regulator.

        SOME STEPS NEED A PERSON. Creating a register, and publishing a blueprint that has not
        been rehearsed, ask a person to confirm via MCP elicitation — only an explicit `accept`
        proceeds. A decline, a silent cancel, and a client that never declared the elicitation
        capability all refuse the same way; declaring the capability is not a promise a person
        will be asked, because a client can auto-cancel every request when running headlessly
        (Claude Code in `-p` mode does exactly this). Expect these tools to refuse cleanly
        rather than proceed unsupervised — this is deliberate, since register creation and an
        unrehearsed publish are both irreversible and establish governance.

        WHAT YOU CAN SEE depends on your token's trust tier and roles; tools you are not entitled
        to use are not listed. If a tool reports an error, read the message — a missing required
        argument is reported as such and names the argument.
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
