// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.ServiceDefaults;
using Sorcha.ServiceDefaults.Storage;
using StackExchange.Redis;

namespace Sorcha.AtomicCache.Extensions;

/// <summary>
/// DI wiring for <see cref="IAtomicDistributedCache"/>. Registers the
/// Redis-backed implementation when a Redis connection string resolves
/// via the SorchaConnections cascade
/// (<c>ConnectionStrings:{ServiceName}:Redis</c> →
/// <c>ConnectionStrings:Sorcha:Redis</c>); falls back to in-memory
/// otherwise. In both cases records the choice with
/// <see cref="IStorageRegistrationLog"/> so Production/Staging fail-fast
/// fires when the audited interface lands on the in-memory fallback.
/// </summary>
public static class AtomicCacheServiceExtensions
{
    /// <summary>
    /// Adds <see cref="IAtomicDistributedCache"/> to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration — read for
    /// the SorchaConnections Redis cascade.</param>
    /// <param name="serviceName">Logical service name used as the
    /// per-service override key (e.g. <c>"Haip"</c> reads
    /// <c>ConnectionStrings:Haip:Redis</c> first).</param>
    public static IServiceCollection AddAtomicDistributedCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var hasResolverConfig =
            !string.IsNullOrWhiteSpace(configuration[$"ConnectionStrings:{serviceName}:Redis"])
            || !string.IsNullOrWhiteSpace(configuration["ConnectionStrings:Sorcha:Redis"]);

        var storageLog = services.GetStorageRegistrationLog();
        var interfaceName = typeof(IAtomicDistributedCache).FullName!;

        if (hasResolverConfig)
        {
            var connectionString = configuration.GetSorchaRedisConnectionString(serviceName);

            // Use TryAdd so multiple consumers in the same service don't double-register.
            // The connection multiplexer is the same for every consumer; one is enough.
            services.TryAddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(connectionString));
            services.TryAddSingleton<IAtomicDistributedCache, RedisAtomicDistributedCache>();
            storageLog.RegisterPersistent(
                interfaceName,
                typeof(RedisAtomicDistributedCache).FullName!,
                "redis");
        }
        else
        {
            services.TryAddSingleton<IAtomicDistributedCache, InMemoryAtomicDistributedCache>();
            storageLog.RegisterInMemory(
                interfaceName,
                typeof(InMemoryAtomicDistributedCache).FullName!,
                $"no Redis connection string in ConnectionStrings:{serviceName}:Redis or ConnectionStrings:Sorcha:Redis");
        }

        return services;
    }
}
