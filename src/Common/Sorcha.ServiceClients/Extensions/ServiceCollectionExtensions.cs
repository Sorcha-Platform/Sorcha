// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Grpc;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Participant;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Validator;
using Sorcha.Register.Service.Grpc;
using Sorcha.Wallet.Service.Grpc;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.ServiceClients.Extensions;

/// <summary>
/// Dependency injection extensions for service clients
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Sorcha service clients
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Registers:
    /// - IWalletServiceClient
    /// - IRegisterServiceClient
    /// - IBlueprintServiceClient
    /// - IPeerServiceClient
    /// - IParticipantServiceClient
    ///
    /// Configuration:
    /// <code>
    /// {
    ///   "ServiceClients": {
    ///     "WalletService": { "Address": "https://localhost:7001", "UseGrpc": false },
    ///     "RegisterService": { "Address": "https://localhost:7002", "UseGrpc": false },
    ///     "BlueprintService": { "Address": "https://localhost:7003", "UseGrpc": false },
    ///     "PeerService": { "Address": "https://localhost:7004", "UseGrpc": true },
    ///     "TenantService": { "Address": "https://localhost:7110" }
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public static IServiceCollection AddServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register service auth client (singleton - caches tokens across requests)
        services.AddHttpClient<ServiceAuthClient>();
        services.AddSingleton<IServiceAuthClient, ServiceAuthClient>();

        // Register delegation token client (scoped - no caching, short-lived tokens)
        services.AddHttpClient<DelegationTokenClient>();
        services.AddScoped<IDelegationTokenClient, DelegationTokenClient>();

        // Register token introspection client (scoped - for on-demand token verification)
        services.AddHttpClient<TokenIntrospectionClient>();
        services.AddScoped<ITokenIntrospectionClient, TokenIntrospectionClient>();

        // Register all service clients with HttpClient factories
        services.AddHttpClient<WalletServiceClient>();
        services.AddScoped<IWalletServiceClient, WalletServiceClient>();

        services.AddHttpClient<RegisterServiceClient>();
        services.AddScoped<IRegisterServiceClient, RegisterServiceClient>();

        services.AddHttpClient<BlueprintServiceClient>();
        services.AddScoped<IBlueprintServiceClient, BlueprintServiceClient>();

        // Peer Service: HttpClient via factory (avoids socket exhaustion), gRPC channel created internally
        services.AddHttpClient<IPeerServiceClient, PeerServiceClient>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var httpAddress = config["ServiceClients:PeerService:HttpAddress"]
                ?? config["ServiceClients:PeerService:Address"]
                ?? "";
            if (!string.IsNullOrEmpty(httpAddress))
            {
                client.BaseAddress = new Uri(httpAddress.TrimEnd('/') + "/");
            }
        });

        services.AddHttpClient<ValidatorServiceClient>();
        services.AddScoped<IValidatorServiceClient, ValidatorServiceClient>();

        services.AddHttpClient<ParticipantServiceClient>();
        services.AddScoped<IParticipantServiceClient, ParticipantServiceClient>();

        // Feature 047: Inbound transaction routing gRPC clients via GrpcClientFactory.
        // Named clients are resolved per-call via GrpcClientFactory.CreateClient<T>(name),
        // which is safe for Singleton consumers (no captive dependency).
        // Aspire service discovery resolves https+http:// URIs at runtime.
        services.AddGrpcClient<RegisterAddressService.RegisterAddressServiceClient>(
            RegisterAddressClient.ClientName, o =>
        {
            o.Address = new Uri(
                configuration["ServiceClients:RegisterService:GrpcAddress"]
                ?? "https+http://register-service");
        });
        services.AddSingleton<IRegisterAddressClient, RegisterAddressClient>();

        services.AddGrpcClient<WalletNotificationService.WalletNotificationServiceClient>(
            WalletNotificationClient.ClientName, o =>
        {
            o.Address = new Uri(
                configuration["ServiceClients:WalletService:GrpcAddress"]
                ?? "https+http://wallet-service");
        });
        services.AddSingleton<IWalletNotificationClient, WalletNotificationClient>();

        services.AddGrpcClient<DocketSyncService.DocketSyncServiceClient>(
            DocketSyncClient.ClientName, o =>
        {
            o.Address = new Uri(
                configuration["ServiceClients:PeerService:GrpcAddress"]
                ?? "https+http://peer-service");
        });
        services.AddSingleton<IDocketSyncClient, DocketSyncClient>();

        return services;
    }

    /// <summary>
    /// Registers individual service client
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddWalletServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IWalletServiceClient, WalletServiceClient>();
        return services;
    }

    /// <summary>
    /// Registers Register Service client with HttpClient factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRegisterServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<RegisterServiceClient>();
        services.AddScoped<IRegisterServiceClient, RegisterServiceClient>();
        return services;
    }

    /// <summary>
    /// Registers Blueprint Service client with HttpClient factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddBlueprintServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<BlueprintServiceClient>();
        services.AddScoped<IBlueprintServiceClient, BlueprintServiceClient>();
        return services;
    }

    /// <summary>
    /// Registers Peer Service client with HttpClient factory for REST and internal gRPC channel
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddPeerServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<IPeerServiceClient, PeerServiceClient>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var httpAddress = config["ServiceClients:PeerService:HttpAddress"]
                ?? config["ServiceClients:PeerService:Address"]
                ?? "";
            if (!string.IsNullOrEmpty(httpAddress))
            {
                client.BaseAddress = new Uri(httpAddress.TrimEnd('/') + "/");
            }
        });
        return services;
    }

    /// <summary>
    /// Registers participant service client
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddParticipantServiceClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<ParticipantServiceClient>();
        services.AddScoped<IParticipantServiceClient, ParticipantServiceClient>();
        return services;
    }

    /// <summary>
    /// Registers DID resolver infrastructure: IDidResolverRegistry and all built-in resolvers
    /// (did:sorcha, did:web, did:key).
    /// </summary>
    public static IServiceCollection AddDidResolvers(this IServiceCollection services)
    {
        // Register individual resolvers
        services.AddSingleton<SorchaDidResolver>();
        services.AddSingleton<KeyDidResolver>();
        services.AddHttpClient<WebDidResolver>();
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<SorchaDidResolver>());
        services.AddSingleton<IDidResolver>(sp => sp.GetRequiredService<KeyDidResolver>());

        // Register the registry and wire up all resolvers
        services.AddSingleton<IDidResolverRegistry>(sp =>
        {
            var registry = new DidResolverRegistry(
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DidResolverRegistry>>());

            foreach (var resolver in sp.GetServices<IDidResolver>())
            {
                registry.Register(resolver);
            }

            // WebDidResolver is transient (HttpClient) — create one instance via factory
            var webResolver = sp.GetRequiredService<WebDidResolver>();
            registry.Register(webResolver);

            return registry;
        });

        return services;
    }
}
