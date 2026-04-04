// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Wallet.Core.Encryption.Interfaces;

namespace Sorcha.Wallet.Providers.Azure.Extensions;

/// <summary>
/// Extension methods for registering Azure Key Vault providers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as the key protection and signing provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAzureKeyVaultProvider(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureKmsOptions>(
            configuration.GetSection(AzureKmsOptions.SectionName));

        services.AddSingleton<IKeyProtectionProvider, AzureKeyProtectionProvider>();
        services.AddSingleton<ISigningProvider, AzureSigningProvider>();

        return services;
    }
}
