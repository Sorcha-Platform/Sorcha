// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sorcha.ServiceClients.SystemWallet;

/// <summary>
/// DI registration extensions for the system wallet signing service.
/// </summary>
/// <remarks>
/// This is an explicit opt-in registration — only services that need system-level
/// signing capability should call <see cref="AddSystemWalletSigning"/>.
/// Requires <c>IWalletServiceClient</c> to be already registered (via <c>AddServiceClients</c>).
/// </remarks>
public static class SystemWalletSigningExtensions
{
    /// <summary>
    /// Registers the system wallet signing service as a singleton with configuration binding.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration (reads "SystemWalletSigning" section)</param>
    /// <returns>Service collection for chaining</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <c>SystemWalletSigning:ValidatorId</c> configuration is missing.
    /// </exception>
    public static IServiceCollection AddSystemWalletSigning(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("SystemWalletSigning");

        var options = new SystemWalletSigningOptions
        {
            ValidatorId = section["ValidatorId"]
                ?? throw new InvalidOperationException(
                    "SystemWalletSigning:ValidatorId configuration is required"),
            MaxSignsPerRegisterPerMinute = int.TryParse(
                section["MaxSignsPerRegisterPerMinute"], out var max) ? max : 10
        };

        // Parse allowed derivation paths from configuration array
        var pathsSection = section.GetSection("AllowedDerivationPaths");
        if (pathsSection.Exists())
        {
            var paths = pathsSection.GetChildren()
                .Select(c => c.Value)
                .Where(v => v is not null)
                .ToArray();

            if (paths.Length > 0)
                options.AllowedDerivationPaths = paths!;
        }

        services.AddSingleton(options);
        services.AddSingleton<ISystemWalletSigningService, SystemWalletSigningService>();
        return services;
    }
}
