// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Default implementation of IStorageProviderFactory that resolves providers from DI.
/// </summary>
public class StorageProviderFactory : IStorageProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StorageConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the StorageProviderFactory.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies.</param>
    /// <param name="options">Storage configuration options.</param>
    public StorageProviderFactory(
        IServiceProvider serviceProvider,
        IOptions<StorageConfiguration> options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public ICacheStore GetCacheStore()
    {
        return _serviceProvider.GetRequiredService<ICacheStore>();
    }

    /// <inheritdoc/>
    public IRepository<TEntity, TId> GetRepository<TEntity, TId>()
        where TEntity : class
        where TId : notnull
    {
        return _serviceProvider.GetRequiredService<IRepository<TEntity, TId>>();
    }

    /// <inheritdoc/>
    public IDocumentStore<TDocument, TId> GetDocumentStore<TDocument, TId>()
        where TDocument : class
        where TId : notnull
    {
        return _serviceProvider.GetRequiredService<IDocumentStore<TDocument, TId>>();
    }

    /// <inheritdoc/>
    public IWormStore<TDocument, TId> GetWormStore<TDocument, TId>()
        where TDocument : class
        where TId : notnull
    {
        return _serviceProvider.GetRequiredService<IWormStore<TDocument, TId>>();
    }

    /// <inheritdoc/>
    public async Task<StorageHealthStatus> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        TierHealthStatus? hotTier = null;
        TierHealthStatus? warmTier = null;
        TierHealthStatus? coldTier = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Check Hot tier (Cache)
        try
        {
            var cacheStore = _serviceProvider.GetService<ICacheStore>();
            if (cacheStore is not null)
            {
                await cacheStore.GetStatisticsAsync(cancellationToken);
                sw.Stop();
                hotTier = TierHealthStatus.Healthy(
                    provider: _configuration.Hot?.Provider ?? "InMemory",
                    responseTimeMs: sw.Elapsed.TotalMilliseconds);
            }
            else
            {
                hotTier = TierHealthStatus.Unhealthy(
                    provider: "None",
                    errorMessage: "No cache store registered");
            }
        }
        catch (Exception ex)
        {
            hotTier = TierHealthStatus.Unhealthy(
                provider: _configuration.Hot?.Provider ?? "Unknown",
                errorMessage: ex.Message);
        }

        // Note: Warm and Cold tier health checks require specific type parameters
        // They should be checked by the consuming service that knows the types
        warmTier = TierHealthStatus.Unhealthy(
            provider: _configuration.Warm?.Relational?.Provider ?? _configuration.Warm?.Documents?.Provider ?? "Unknown",
            errorMessage: "Health check not implemented — requires service-specific type parameters");

        coldTier = TierHealthStatus.Unhealthy(
            provider: "MongoDB",
            errorMessage: "Health check not implemented — requires service-specific type parameters");

        var isHealthy = (hotTier?.IsHealthy ?? true)
                     && (warmTier?.IsHealthy ?? true)
                     && (coldTier?.IsHealthy ?? true);

        return new StorageHealthStatus(
            IsHealthy: isHealthy,
            HotTier: hotTier,
            WarmTier: warmTier,
            ColdTier: coldTier,
            Timestamp: DateTime.UtcNow);
    }
}
