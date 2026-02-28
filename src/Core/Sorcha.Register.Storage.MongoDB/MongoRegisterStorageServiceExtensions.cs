// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Sorcha.Register.Core.Storage;

namespace Sorcha.Register.Storage.MongoDB;

/// <summary>
/// Extension methods for registering MongoDB Register storage services.
/// </summary>
public static class MongoRegisterStorageServiceExtensions
{
    /// <summary>
    /// Adds MongoDB Register storage to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section for MongoDB settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMongoRegisterStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MongoRegisterStorageConfiguration>(
            configuration.GetSection("RegisterStorage:MongoDB"));

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
            return new MongoClient(config.ConnectionString);
        });
        services.TryAddSingleton<IRegisterRepository, MongoRegisterRepository>();

        return services;
    }

    /// <summary>
    /// Adds MongoDB Register storage with explicit configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMongoRegisterStorage(
        this IServiceCollection services,
        Action<MongoRegisterStorageConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
            return new MongoClient(config.ConnectionString);
        });
        services.TryAddSingleton<IRegisterRepository, MongoRegisterRepository>();

        return services;
    }

    /// <summary>
    /// Adds read-only MongoDB Register storage. Registers IReadOnlyRegisterRepository only —
    /// write operations are not available. Use this for services that need to read register
    /// data but do not own the register database (e.g., Validator Service).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section for MongoDB settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddReadOnlyMongoRegisterStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MongoRegisterStorageConfiguration>(
            configuration.GetSection("RegisterStorage:MongoDB"));

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
            return new MongoClient(config.ConnectionString);
        });
        services.TryAddSingleton<IReadOnlyRegisterRepository, MongoRegisterRepository>();

        return services;
    }

    /// <summary>
    /// Adds MongoDB Register storage with connection string.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Optional database name (defaults to sorcha_register).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMongoRegisterStorage(
        this IServiceCollection services,
        string connectionString,
        string? databaseName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        services.Configure<MongoRegisterStorageConfiguration>(config =>
        {
            config.ConnectionString = connectionString;
            if (databaseName is not null)
            {
                config.DatabaseName = databaseName;
            }
        });

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<MongoRegisterStorageConfiguration>>().Value;
            return new MongoClient(config.ConnectionString);
        });
        services.TryAddSingleton<IRegisterRepository, MongoRegisterRepository>();

        return services;
    }
}
