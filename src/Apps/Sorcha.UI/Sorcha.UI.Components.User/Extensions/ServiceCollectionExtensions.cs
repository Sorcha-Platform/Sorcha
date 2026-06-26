// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.UI.Components.User.Services.Verification;
using Sorcha.Verifier.Engine;

namespace Sorcha.UI.Components.User.Extensions;

/// <summary>
/// Dependency-injection extension points for the shared user-facing UI component library.
/// Host applications (Sorcha.UI.* web apps and Sorcha.Wallet.Pwa PWA) call
/// <see cref="AddSorchaUserComponents"/> from their <c>Program.cs</c> to register the
/// services the shared components consume via <c>@inject</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services consumed by components in <c>Sorcha.UI.Components.User</c>,
    /// including the verification seams introduced in Feature 163 (PR B2-components).
    /// All registrations use <c>TryAdd*</c> semantics so a host can override the defaults
    /// before or after calling this method (FR-005/FR-006, R-005).
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configuration">Application configuration; used to bind <c>VerifierPresets</c> options.</param>
    public static IServiceCollection AddSorchaUserComponents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Verify seams — Feature 163 (PR B2-components).
        // TryAdd* ensures a host override registered before this call wins.
        services.TryAddSingleton<IVerificationPresetCatalogue, DefaultPresetCatalogue>();
        services.Configure<VerifierPresetsOptions>(configuration.GetSection("VerifierPresets"));

        services.TryAddSingleton<IVerificationTransport, NotConfiguredVerificationTransport>();

        // Feature 164 B3 (US1): typed HTTP client for the HAIP verifier endpoints. Hosts that override
        // IVerificationTransport with HaipVerificationTransport depend on this registration. Registered
        // always so HaipVerificationTransport can be resolved without host-specific HttpClient setup.
        services.AddHttpClient<IHaipVerifierClient, HaipVerifierClient>();

        // Guard so a host that registered IRegisterAnchorClient before calling AddSorchaUserComponents
        // keeps its own registration rather than being shadowed by the typed HttpClient registration.
        if (services.All(d => d.ServiceType != typeof(IRegisterAnchorClient)))
        {
            services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>();
        }

        return services;
    }
}
