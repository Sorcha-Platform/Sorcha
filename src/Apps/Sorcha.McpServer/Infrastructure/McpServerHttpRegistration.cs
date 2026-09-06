// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Extensions;
using Sorcha.ServiceDefaults;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// The service registrations the HTTP transport uses, in one callable place so a test can
/// build the same container the server builds. Extracted because the HTTP branch silently
/// omitted <c>IMcpSessionService</c> while eleven tools still demanded it, and nothing could
/// observe that from outside <c>Program.cs</c>'s top-level statements.
/// </summary>
public static class McpServerHttpRegistration
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IJwtValidationHandler, JwtValidationHandler>();
        services.AddHttpContextAccessor();
        services.AddSingleton<ICallerContext, HttpCallerContext>();

        // Every tool depends on the shared MCP infrastructure (authorization, error handling,
        // audit, availability tracking) regardless of transport. Without this, an activation gate
        // built from ConfigureServices alone would fail every tool on IMcpAuthorizationService —
        // a container shape the real HTTP transport never has, since Program.cs always calls this
        // immediately alongside the registrations above.
        RegisterMcpInfrastructure(services, configuration);

        // Must mirror production exactly, including the typed service clients. A test container
        // that omitted them would activate tools against a shape the server never builds, and the
        // activation gate would pass while the deployed surface stayed dead — the precise failure
        // this task exists to end.
        RegisterServiceClients(services, configuration);
    }

    /// <summary>
    /// The MCP-specific infrastructure services (authorization, rate limiting, audit, error
    /// handling, availability tracking, metrics) shared by both transports. Moved here alongside
    /// <see cref="RegisterServiceClients"/> for the same reason: one definition, callable from a
    /// test that must build the same container the server builds.
    /// </summary>
    internal static void RegisterMcpInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IMcpAuthorizationService, McpAuthorizationService>();
        services.AddSingleton<IRateLimitService, RateLimitService>();
        services.AddSingleton<IToolAuditService, ToolAuditService>();
        services.AddSingleton<IMcpErrorHandler, McpErrorHandler>();
        services.AddSingleton<IServiceAvailabilityTracker, ServiceAvailabilityTracker>();

        // Spec 139 US5: per-invocation observability. McpMetrics needs IMeterFactory (AddMetrics)
        // and is registered as a singleton; the central call-tool audit filter records every
        // invocation through ToolAuditService, which emits these metrics. Add the Sorcha.Mcp
        // meter to the OTel meter provider so the counters/histogram export when an OTLP endpoint
        // is configured — the exporter itself is only wired when OTEL_EXPORTER_OTLP_ENDPOINT is
        // set (silent otherwise).
        services.AddMetrics();
        services.AddSingleton<McpMetrics>();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(McpMetrics.MeterName));

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    /// <summary>
    /// Registers Sorcha's typed service clients and attaches the caller-token-forwarding handler
    /// to each one so the caller's bearer rides every backend call. Shared by both transports —
    /// moved here (out of <c>Program.cs</c>) so there is exactly one definition.
    /// </summary>
    internal static void RegisterServiceClients(IServiceCollection services, IConfiguration configuration)
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

        // Feature 140 Wave 1: the Peer typed client is registered interface-first in AddServiceClients
        // (AddHttpClient<IPeerServiceClient, PeerServiceClient>), so its named HttpClient is keyed by the
        // interface. Re-open that same registration to append the forwarding handler so register
        // subscribe/unsubscribe calls ride the caller's bearer to the gateway.
        services.AddHttpClient<Sorcha.ServiceClients.Peer.IPeerServiceClient, Sorcha.ServiceClients.Peer.PeerServiceClient>()
            .AddHttpMessageHandler<CallerTokenForwardingHandler>();

        // Feature 140 Wave 2: the HAIP typed client is also registered interface-first in
        // AddServiceClients (AddHttpClient<IHaipServiceClient, HaipServiceClient>). Re-open that
        // same named registration to append the forwarding handler so the credential-offer /
        // presentation-request tools ride the caller's bearer to the gateway. The Blueprint client
        // (used by the presentation-status + credential-lifecycle tools) is already covered above.
        services.AddHttpClient<Sorcha.ServiceClients.Haip.IHaipServiceClient, Sorcha.ServiceClients.Haip.HaipServiceClient>()
            .AddHttpMessageHandler<CallerTokenForwardingHandler>();

        // Feature 140 Wave 3: the citizen self-service tools are CONSUMER tier and MUST act as the
        // calling citizen. The CitizenWalletClient and RegisterInvitationServiceClient are registered
        // concrete-type-keyed in AddServiceClients (AddHttpClient<CitizenWalletClient>() /
        // AddHttpClient<RegisterInvitationServiceClient>()) — re-open those same named registrations to
        // append the forwarding handler so the consumer's bearer rides every my-credentials /
        // my-devices / my-presentations / my-invitations call. The Tenant client (used by the
        // my-persona tool) already has the handler attached above.
        services.AddHttpClient<Sorcha.ServiceClients.CitizenWallet.CitizenWalletClient>()
            .AddHttpMessageHandler<CallerTokenForwardingHandler>();
        services.AddHttpClient<Sorcha.ServiceClients.Invitation.RegisterInvitationServiceClient>()
            .AddHttpMessageHandler<CallerTokenForwardingHandler>();

        // Feature 140 Wave 4: the platform-administration depth tools route org-status, platform-settings,
        // org-user-audit, user-provision and user-password-reset through the Tenant client (already covered
        // above) and validator start/stop through the Validator typed client. The ValidatorServiceClient is
        // registered concrete-type-keyed in AddServiceClients (AddHttpClient<ValidatorServiceClient>()) —
        // re-open that same named registration to append the forwarding handler so the admin's bearer rides
        // every validator-control call to the gateway (which enforces the SystemAdmin gate server-side).
        services.AddHttpClient<Sorcha.ServiceClients.Validator.ValidatorServiceClient>()
            .AddHttpMessageHandler<CallerTokenForwardingHandler>();
    }
}
