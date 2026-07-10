// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.CitizenStatusList;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Evm;
using Sorcha.ServiceClients.Invitation;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Subscription;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.ServiceClients.Http.Extensions;

/// <summary>
/// Dependency injection extensions for HTTP-only service clients.
/// Use this in mobile/MAUI apps that do not need gRPC. Server-side callers should
/// use <c>AddServiceClients</c> from <c>Sorcha.ServiceClients</c> instead, which
/// calls this method internally and adds gRPC registrations.
/// </summary>
public static class HttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers all HTTP service clients (auth, REST, DID resolvers).
    /// Does NOT register gRPC clients or Peer service client.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddHttpServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Auth clients
        services.AddHttpClient<ServiceAuthClient>();
        services.AddSingleton<IServiceAuthClient, ServiceAuthClient>();

        services.AddHttpClient<DelegationTokenClient>();
        services.AddScoped<IDelegationTokenClient, DelegationTokenClient>();

        services.AddHttpClient<TokenIntrospectionClient>();
        services.AddScoped<ITokenIntrospectionClient, TokenIntrospectionClient>();

        // HTTP service clients
        services.AddHttpClient<WalletServiceClient>();
        services.AddScoped<IWalletServiceClient, WalletServiceClient>();

        services.AddHttpClient<RegisterServiceClient>();
        services.AddScoped<IRegisterServiceClient, RegisterServiceClient>();

        services.AddHttpClient<BlueprintServiceClient>();
        services.AddScoped<IBlueprintServiceClient, BlueprintServiceClient>();

        services.AddHttpClient<ValidatorServiceClient>();
        services.AddScoped<IValidatorServiceClient, ValidatorServiceClient>();

        services.AddHttpClient<ParticipantServiceClient>();
        services.AddScoped<IParticipantServiceClient, ParticipantServiceClient>();

        // Spec 139 US4: typed Tenant client for MCP org/user/token reconciliation.
        services.AddHttpClient<TenantServiceClient>();
        services.AddScoped<ITenantServiceClient, TenantServiceClient>();

        services.AddHttpClient<SubscriptionServiceClient>();
        services.AddScoped<ISubscriptionServiceClient, SubscriptionServiceClient>();

        // Feature 060: Passkey public key retrieval for recovery key wrapping
        services.AddHttpClient<Passkey.PasskeyServiceClient>();
        services.AddScoped<Passkey.IPasskeyServiceClient, Passkey.PasskeyServiceClient>();

        // Feature 114: Citizen wallet device registry on Tenant Service
        services.AddHttpClient<PlatformUserDeviceClient>();
        services.AddScoped<IPlatformUserDeviceClient, PlatformUserDeviceClient>();

        // Feature 118 / US3: durable user inbox writer — POSTs entries to Tenant Service.
        services.AddHttpClient<Inbox.PlatformInboxClient>();
        services.AddScoped<Inbox.IPlatformInboxClient, Inbox.PlatformInboxClient>();

        // Feature 149: resolve an org's canonical operational wallet address (A) from Tenant
        // so the Wallet Service anchors the VC-issuer DID on did:sorcha:org:{A}.
        services.AddHttpClient<OrgInfo.OrgInfoClient>();
        services.AddScoped<OrgInfo.IOrgInfoClient, OrgInfo.OrgInfoClient>();

        // Feature 114: Citizen wallet client used by the PWA to call Wallet Service.
        // Caller-supplied JWT (no service-principal injection — citizen audience required).
        services.AddHttpClient<CitizenWalletClient>();
        services.AddScoped<ICitizenWalletClient, CitizenWalletClient>();

        // Register-invitation client (targets the Tenant Service). Auth rides the caller's
        // pipeline (no service-principal injection); the MCP server's citizen self-service
        // tools (Feature 140 Wave 3) forward the consumer's bearer so the listing is scoped
        // to the caller's organisation.
        services.AddHttpClient<RegisterInvitationServiceClient>();
        services.AddScoped<IRegisterInvitationServiceClient, RegisterInvitationServiceClient>();

        // Feature 114: Tenant→Wallet S2S client for citizen device revocation
        // (status-list bit flip + SignalR DeviceRevoked broadcast).
        services.AddHttpClient<CitizenStatusListClient>();
        services.AddScoped<ICitizenStatusListClient, CitizenStatusListClient>();

        // DID resolvers
        services.AddDidResolvers();

        // Feature 179 — read-only EVM RPC for on-chain did:ethr resolution. Server-side only (this
        // method is not called by the WASM PWA), so EthrDidResolver stays offline in the browser. Safe
        // to register unconditionally: with no DidResolver:Ethr:Rpc config it resolves NotConfigured and
        // did:ethr falls back to the offline default document.
        services.AddEvmRpc(configuration);

        return services;
    }

    /// <summary>
    /// Registers DID resolver infrastructure: IDidResolverRegistry and all built-in resolvers
    /// (did:sorcha, did:web, did:key, did:jwk, and the address-form did:pkh / did:ethr — Feature 178),
    /// the Feature 120 cross-resolution cache, and OTel meters.
    /// </summary>
    public static IServiceCollection AddDidResolvers(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // SorchaDidResolver depends on IWalletServiceClient (Scoped), so it must also be Scoped.
        // KeyDidResolver has no scoped dependencies — Singleton is fine.
        services.AddScoped<SorchaDidResolver>();
        services.AddSingleton<KeyDidResolver>();
        services.AddSingleton<JwkDidResolver>();
        services.AddSingleton<PkhDidResolver>();
        // EthrDidResolver takes an OPTIONAL Erc1056Registry — present only in server hosts that called
        // AddEvmRpc (Feature 179). GetService returns null in the WASM/offline composition, so
        // EthrDidResolver falls back to the offline default document. A factory is required because the
        // built-in DI container does not honour the constructor's default parameter value.
        services.AddSingleton<EthrDidResolver>(sp => new EthrDidResolver(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EthrDidResolver>>(),
            sp.GetService<Sorcha.ServiceClients.Evm.Erc1056Registry>()));
        services.AddHttpClient<WebDidResolver>();
        services.AddScoped<IDidResolver>(sp => sp.GetRequiredService<SorchaDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<KeyDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<JwkDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<PkhDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<EthrDidResolver>());

        // Registry must be Scoped to resolve the Scoped SorchaDidResolver. Production
        // wiring passes the Singleton DidResolverCache + DidResolverMetrics so the
        // cross-resolution algorithm (Feature 120 US4) honours per-method TTLs and
        // emits OTel counters.
        services.AddScoped<IDidResolverRegistry>(sp =>
        {
            var registry = new DidResolverRegistry(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DidResolverRegistry>>(),
                sp.GetRequiredService<DidResolverCache>(),
                sp.GetRequiredService<DidResolverMetrics>());

            foreach (var resolver in sp.GetServices<IDidResolver>())
            {
                registry.Register(resolver);
            }

            var webResolver = sp.GetRequiredService<WebDidResolver>();
            registry.Register(webResolver);

            return registry;
        });

        // Feature 120 — cross-resolution cache (singleton; per-method TTLs) and OTel meter.
        if (configuration is not null)
        {
            services.Configure<DidResolverCacheOptions>(
                configuration.GetSection(DidResolverCacheOptions.SectionName));
        }
        else
        {
            services.AddOptions<DidResolverCacheOptions>();
        }
        services.AddSingleton<DidResolverCache>();
        services.AddSingleton<DidResolverMetrics>();

        return services;
    }

    /// <summary>
    /// Registers read-only EVM RPC for on-chain <c>did:ethr</c> resolution (Feature 179) — the
    /// <see cref="IEvmRpcClient"/> transport and the <see cref="Erc1056Registry"/> reader. Call this
    /// <b>only from server hosts</b>; the Blazor WASM PWA deliberately omits it so <c>did:ethr</c>
    /// resolves to the offline default document (the server is authoritative for rotation). Binds
    /// <c>DidResolver:Ethr</c> plus the shared <c>DidResolver:AllowPrivateAddresses</c> flag.
    /// </summary>
    public static IServiceCollection AddEvmRpc(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<EvmRpcOptions>(options =>
        {
            configuration.GetSection("DidResolver:Ethr").Bind(options);
            options.AllowPrivateAddresses = configuration.GetValue<bool>("DidResolver:AllowPrivateAddresses");
        });

        services.AddHttpClient<IEvmRpcClient, EvmRpcClient>();
        services.AddSingleton<Erc1056Registry>();

        return services;
    }

    /// <summary>
    /// Wires the <see cref="DidSorchaCacheInvalidationService"/> hosted service that drains
    /// confirmed-transaction events into <see cref="DidResolverCache"/> invalidations.
    /// Callers MUST register an <see cref="IDidCacheTransactionEventSource"/> implementation
    /// (typically a thin adapter over the service-level <c>IEventSubscriber</c>).
    /// </summary>
    public static IServiceCollection AddDidSorchaCacheInvalidation(this IServiceCollection services)
    {
        services.AddHostedService<DidSorchaCacheInvalidationService>();
        return services;
    }
}
