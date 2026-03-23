// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Extensions;

/// <summary>
/// Extension methods for registering ValidatorRegistry services
/// </summary>
public static class ValidatorRegistryExtensions
{
    /// <summary>
    /// Adds the validator registry service with Redis backing
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddValidatorRegistry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<ValidatorRegistryConfiguration>(
            configuration.GetSection(ValidatorRegistryConfiguration.SectionName));

        // Register the service as scoped (depends on scoped IRegisterServiceClient)
        // State is maintained in Redis, not in-memory, so scoped lifetime is appropriate
        services.AddScoped<IValidatorRegistry, ValidatorRegistry>();

        // Wallet sequence repository for replay protection (SEC-AUDIT 4.2)
        services.AddSingleton<IWalletSequenceRepository, WalletSequenceRepository>();

        return services;
    }
}
