// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Storage.EFCore;

/// <summary>
/// Extension methods for registering EF Core storage services.
/// </summary>
public static class EFCoreServiceExtensions
{
    /// <summary>
    /// Adds a PostgreSQL DbContext with the storage configuration.
    /// </summary>
    /// <typeparam name="TContext">DbContext type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration instance.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : DbContext
    {
        var connectionString = configuration.GetSection("Storage:Warm:Relational:ConnectionString").Value;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is not configured. " +
                "Set 'Storage:Warm:Relational:ConnectionString' in configuration.");
        }

        services.AddDbContext<TContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
        });

        return services;
    }

    /// <summary>
    /// Adds a PostgreSQL DbContext with explicit connection string.
    /// </summary>
    /// <typeparam name="TContext">DbContext type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    /// <param name="enableSensitiveDataLogging">Enable sensitive data logging (dev only).</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlDbContext<TContext>(
        this IServiceCollection services,
        string connectionString,
        bool enableSensitiveDataLogging = false)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            if (enableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }

    /// <summary>
    /// Adds an EF Core repository for a specific entity type.
    /// </summary>
    /// <typeparam name="TEntity">Entity type.</typeparam>
    /// <typeparam name="TId">Primary key type.</typeparam>
    /// <typeparam name="TContext">DbContext type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="idSelector">Function to extract ID from entity.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddEFCoreRepository<TEntity, TId, TContext>(
        this IServiceCollection services,
        Func<TEntity, TId> idSelector)
        where TEntity : class
        where TId : notnull
        where TContext : DbContext
    {
        services.AddScoped<IRepository<TEntity, TId>>(sp =>
        {
            var context = sp.GetRequiredService<TContext>();
            return new EFCoreRepository<TEntity, TId, TContext>(context, idSelector);
        });

        return services;
    }
}
