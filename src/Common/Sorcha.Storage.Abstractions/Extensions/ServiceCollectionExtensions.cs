// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Extension methods for registering storage services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds storage configuration from configuration section.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration instance.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddStorageConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageConfiguration>(
            configuration.GetSection(StorageConfiguration.SectionName));

        return services;
    }

    /// <summary>
    /// Adds storage configuration with options action.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddStorageConfiguration(
        this IServiceCollection services,
        Action<StorageConfiguration> configureOptions)
    {
        services.Configure(configureOptions);
        return services;
    }

}
