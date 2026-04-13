// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.AddressLookup.Providers;

namespace Sorcha.AddressLookup;

/// <summary>
/// DI registration extensions for <see cref="AddressLookupService"/> and its
/// providers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Sorcha address lookup service plus the default
    /// <see cref="PostcodesIoProvider"/>. Conditionally registers
    /// <see cref="OsPlacesProvider"/> when an API key is configured at
    /// <c>Tenant:AddressLookup:OsPlaces:ApiKey</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>postcodes.io</b> is always registered because it's free, key-less,
    /// rate-limit-free, and MIT-licensed — safe to ship as default-on so that
    /// the form renderer has something to dispatch to without any deployment
    /// configuration.
    /// </para>
    /// <para>
    /// <b>OS Places</b> requires a licensed API key from Ordnance Survey.
    /// Deployments that have one get full street-address autocomplete; the
    /// rest fall through to postcodes.io validate-only metadata. Switching
    /// the key on or off at runtime is not supported — the DI graph is
    /// determined at startup.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSorchaAddressLookup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Default provider — always on.
        services.AddHttpClient<PostcodesIoProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.postcodes.io/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IAddressLookupProvider>(sp =>
            sp.GetRequiredService<PostcodesIoProvider>());

        // Opt-in OS Places provider (requires API key).
        services.Configure<OsPlacesOptions>(configuration.GetSection(OsPlacesOptions.SectionName));
        var osPlacesSection = configuration.GetSection(OsPlacesOptions.SectionName);
        var osPlacesApiKey = osPlacesSection["ApiKey"];
        if (!string.IsNullOrWhiteSpace(osPlacesApiKey))
        {
            var baseUrl = osPlacesSection["BaseUrl"] ?? "https://api.os.uk/search/places/v1/";
            services.AddHttpClient<OsPlacesProvider>(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddSingleton<IAddressLookupProvider>(sp =>
                sp.GetRequiredService<OsPlacesProvider>());
        }

        // Composition root.
        services.AddSingleton<AddressLookupService>();

        return services;
    }
}
