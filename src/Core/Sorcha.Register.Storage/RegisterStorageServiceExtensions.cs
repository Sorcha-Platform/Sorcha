// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Storage.InMemory;
using Sorcha.Storage.Abstractions;
using Sorcha.Storage.Abstractions.Caching;

namespace Sorcha.Register.Storage;

/// <summary>
/// Extension methods for registering Register storage services.
/// </summary>
public static class RegisterStorageServiceExtensions
{
    /// <summary>
    /// Adds Register storage services with configuration from appsettings.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration instance.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddRegisterStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<RegisterStorageConfiguration>(
            configuration.GetSection(RegisterStorageConfiguration.SectionName));

        return services.AddRegisterStorageCore();
    }

    /// <summary>
    /// Adds Register storage services with explicit configuration.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddRegisterStorage(
        this IServiceCollection services,
        Action<RegisterStorageConfiguration> configure)
    {
        services.Configure(configure);
        return services.AddRegisterStorageCore();
    }

    /// <summary>
    /// Adds Register storage services using in-memory implementations (for testing).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryRegisterStorage(this IServiceCollection services)
    {
        services.Configure<RegisterStorageConfiguration>(config =>
        {
            config.UseInMemoryStorage = true;
        });

        return services.AddRegisterStorageCore();
    }

    private static IServiceCollection AddRegisterStorageCore(this IServiceCollection services)
    {
        // Register the inner repository (actual storage)
        services.AddSingleton<InMemoryRegisterRepository>();

        // Register cache store (could be in-memory or Redis based on config)
        services.AddSingleton<ICacheStore>(sp =>
        {
            var config = sp.GetService<IOptions<RegisterStorageConfiguration>>()?.Value
                ?? new RegisterStorageConfiguration();

            if (config.UseInMemoryStorage)
            {
                return new Sorcha.Storage.InMemory.InMemoryCacheStore();
            }

            // For production, you would resolve Redis cache store here
            // For now, fall back to in-memory
            return new Sorcha.Storage.InMemory.InMemoryCacheStore();
        });

        // Register WORM store for dockets
        services.AddSingleton<IWormStore<DocketHeader, ulong>>(sp =>
        {
            var config = sp.GetService<IOptions<RegisterStorageConfiguration>>()?.Value
                ?? new RegisterStorageConfiguration();

            // Use in-memory WORM store
            return new Sorcha.Storage.InMemory.InMemoryWormStore<DocketHeader, ulong>(d => d.Id);
        });

        // Register verified cache for dockets
        services.AddSingleton<IVerifiedCache<DocketHeader, ulong>>(sp =>
        {
            var cacheStore = sp.GetRequiredService<ICacheStore>();
            var wormStore = sp.GetRequiredService<IWormStore<DocketHeader, ulong>>();
            var config = sp.GetService<IOptions<RegisterStorageConfiguration>>()?.Value
                ?? new RegisterStorageConfiguration();
            var logger = sp.GetService<ILogger<VerifiedCache<DocketHeader, ulong>>>();

            var cacheConfig = Options.Create(config.DocketCacheConfiguration);

            return new VerifiedCache<DocketHeader, ulong>(
                cacheStore,
                wormStore,
                d => d.Id,
                cacheConfig,
                d => d.Hash, // Hash selector for verification
                logger);
        });

        // Register the cached repository
        services.AddSingleton<IRegisterRepository>(sp =>
        {
            var innerRepo = sp.GetRequiredService<InMemoryRegisterRepository>();
            var docketCache = sp.GetService<IVerifiedCache<DocketHeader, ulong>>();
            var cacheStore = sp.GetRequiredService<ICacheStore>();
            var options = sp.GetService<IOptions<RegisterStorageConfiguration>>()
                ?? Options.Create(new RegisterStorageConfiguration());
            var logger = sp.GetService<ILogger<CachedRegisterRepository>>();

            return new CachedRegisterRepository(
                innerRepo,
                docketCache,
                cacheStore,
                options,
                logger);
        });

        // Register cache warming hosted service if enabled
        services.AddHostedService<DocketCacheWarmingService>();

        return services;
    }

    /// <summary>
    /// Adds cache warming service for Register dockets.
    /// </summary>
    public static IServiceCollection AddDocketCacheWarming(this IServiceCollection services)
    {
        services.AddHostedService<DocketCacheWarmingService>();
        return services;
    }
}
