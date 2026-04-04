// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Grpc;
using Sorcha.ServiceClients.Http.Extensions;
using Sorcha.ServiceClients.Peer;
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
    /// Registers all Sorcha service clients (HTTP + gRPC).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Delegates HTTP client registrations to <see cref="HttpServiceCollectionExtensions.AddHttpServiceClients"/>
    /// from <c>Sorcha.ServiceClients.Http</c>, then adds gRPC and Peer client registrations.
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
        // Register all HTTP clients (auth, REST, DID resolvers) from ServiceClients.Http
        services.AddHttpServiceClients(configuration);

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
    /// Registers DID resolver infrastructure. Forwards to
    /// <see cref="HttpServiceCollectionExtensions.AddDidResolvers"/> in ServiceClients.Http.
    /// </summary>
    public static IServiceCollection AddDidResolvers(this IServiceCollection services)
        => HttpServiceCollectionExtensions.AddDidResolvers(services);
}
