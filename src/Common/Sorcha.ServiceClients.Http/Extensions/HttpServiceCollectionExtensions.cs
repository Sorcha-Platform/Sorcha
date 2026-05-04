// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.CitizenStatusList;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Events;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Subscription;
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

        services.AddHttpClient<EventServiceClient>();
        services.AddScoped<IEventServiceClient, EventServiceClient>();

        services.AddHttpClient<SubscriptionServiceClient>();
        services.AddScoped<ISubscriptionServiceClient, SubscriptionServiceClient>();

        // Feature 060: Passkey public key retrieval for recovery key wrapping
        services.AddHttpClient<Passkey.PasskeyServiceClient>();
        services.AddScoped<Passkey.IPasskeyServiceClient, Passkey.PasskeyServiceClient>();

        // Feature 114: Citizen wallet device registry on Tenant Service
        services.AddHttpClient<PlatformUserDeviceClient>();
        services.AddScoped<IPlatformUserDeviceClient, PlatformUserDeviceClient>();

        // Feature 114: Citizen wallet client used by the PWA to call Wallet Service.
        // Caller-supplied JWT (no service-principal injection — citizen audience required).
        services.AddHttpClient<CitizenWalletClient>();
        services.AddScoped<ICitizenWalletClient, CitizenWalletClient>();

        // Feature 114: Tenant→Wallet S2S client for citizen device revocation
        // (status-list bit flip + SignalR DeviceRevoked broadcast).
        services.AddHttpClient<CitizenStatusListClient>();
        services.AddScoped<ICitizenStatusListClient, CitizenStatusListClient>();

        // DID resolvers
        services.AddDidResolvers();

        return services;
    }

    /// <summary>
    /// Registers DID resolver infrastructure: IDidResolverRegistry and all built-in resolvers
    /// (did:sorcha, did:web, did:key).
    /// </summary>
    public static IServiceCollection AddDidResolvers(this IServiceCollection services)
    {
        // SorchaDidResolver depends on IWalletServiceClient (Scoped), so it must also be Scoped.
        // KeyDidResolver has no scoped dependencies — Singleton is fine.
        services.AddScoped<SorchaDidResolver>();
        services.AddSingleton<KeyDidResolver>();
        services.AddHttpClient<WebDidResolver>();
        services.AddScoped<IDidResolver>(sp => sp.GetRequiredService<SorchaDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<KeyDidResolver>());

        // Registry must be Scoped to resolve the Scoped SorchaDidResolver
        services.AddScoped<IDidResolverRegistry>(sp =>
        {
            var registry = new DidResolverRegistry(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DidResolverRegistry>>());

            foreach (var resolver in sp.GetServices<IDidResolver>())
            {
                registry.Register(resolver);
            }

            var webResolver = sp.GetRequiredService<WebDidResolver>();
            registry.Register(webResolver);

            return registry;
        });

        return services;
    }
}
