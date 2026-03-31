// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering service integration components.
/// </summary>
public static class ServiceIntegrationExtensions
{
    /// <summary>
    /// Adds DocketDistributor services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddDocketDistributor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register configuration
        services.Configure<DocketDistributorConfiguration>(
            configuration.GetSection(DocketDistributorConfiguration.SectionName));

        // Register as scoped (matches service client lifetimes)
        services.AddScoped<IDocketDistributor, DocketDistributor>();

        // Receipt generation (called by DocketDistributor after docket persistence)
        services.AddScoped<IReceiptGenerator, ReceiptGenerator>();

        return services;
    }

    /// <summary>
    /// Adds TransactionReceiver services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddTransactionReceiver(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register configuration
        services.Configure<TransactionReceiverConfiguration>(
            configuration.GetSection(TransactionReceiverConfiguration.SectionName));

        // Register as singleton for shared state (known transactions cache)
        services.AddSingleton<ITransactionReceiver, TransactionReceiver>();

        return services;
    }

    /// <summary>
    /// Adds BlueprintFetcher services to the service collection.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddBlueprintFetcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register configuration
        services.Configure<BlueprintFetcherConfiguration>(
            configuration.GetSection(BlueprintFetcherConfiguration.SectionName));

        // Register as scoped (matches service client lifetimes)
        services.AddScoped<IBlueprintFetcher, BlueprintFetcher>();

        return services;
    }

    /// <summary>
    /// Adds all service integration components.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddServiceIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDocketDistributor(configuration);
        services.AddTransactionReceiver(configuration);
        services.AddBlueprintFetcher(configuration);

        return services;
    }
}
