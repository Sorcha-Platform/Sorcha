// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.InMemory;

/// <summary>
/// Extension methods for registering in-memory storage services.
/// </summary>
public static class InMemoryServiceExtensions
{
    /// <summary>
    /// Adds in-memory cache store implementation.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="defaultExpiration">Default expiration for cache entries.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryCacheStore(
        this IServiceCollection services,
        TimeSpan? defaultExpiration = null)
    {
        services.AddSingleton<ICacheStore>(new InMemoryCacheStore(defaultExpiration));
        return services;
    }

    /// <summary>
    /// Adds in-memory repository implementation.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <typeparam name="TId">Primary key type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="idSelector">Function to extract ID from entity.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryRepository<TEntity, TId>(
        this IServiceCollection services,
        Func<TEntity, TId> idSelector)
        where TEntity : class
        where TId : notnull
    {
        services.AddSingleton<IRepository<TEntity, TId>>(
            new InMemoryRepository<TEntity, TId>(idSelector));
        return services;
    }

    /// <summary>
    /// Adds in-memory document store implementation.
    /// </summary>
    /// <typeparam name="TDocument">Document type.</typeparam>
    /// <typeparam name="TId">Document identifier type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryDocumentStore<TDocument, TId>(
        this IServiceCollection services,
        Func<TDocument, TId> idSelector)
        where TDocument : class
        where TId : notnull
    {
        services.AddSingleton<IDocumentStore<TDocument, TId>>(
            new InMemoryDocumentStore<TDocument, TId>(idSelector));
        return services;
    }

    /// <summary>
    /// Adds in-memory WORM store implementation.
    /// </summary>
    /// <typeparam name="TDocument">Document type.</typeparam>
    /// <typeparam name="TId">Document identifier type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="idSelector">Function to extract ID from document.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryWormStore<TDocument, TId>(
        this IServiceCollection services,
        Func<TDocument, TId> idSelector)
        where TDocument : class
        where TId : notnull
    {
        services.AddSingleton<IWormStore<TDocument, TId>>(
            new InMemoryWormStore<TDocument, TId>(idSelector));
        return services;
    }

    /// <summary>
    /// Adds in-memory cache store as the default storage provider.
    /// For document and WORM stores, use the typed registration methods
    /// (<see cref="AddInMemoryDocumentStore{TDocument, TId}"/> and
    /// <see cref="AddInMemoryWormStore{TDocument, TId}"/>).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryStorageProviders(
        this IServiceCollection services)
    {
        services.AddInMemoryCacheStore();
        return services;
    }
}
