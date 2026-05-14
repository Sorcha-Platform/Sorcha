// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sorcha.Verifier.Services;
using Sorcha.ServiceClients.Http.Extensions;

namespace Sorcha.Verifier.Extensions;

/// <summary>
/// DI extensions for the Sorcha citizen reference verifier (Feature 114).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers verifier services — status list cache, presentation request
    /// builder, session store, and the VP validator.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration. Required for Feature 120
    /// hardening (<c>IssuerSignature:Required</c>); when absent, <c>RequireIssuerSignature</c>
    /// defaults to <c>true</c> per FR-019.</param>
    public static IServiceCollection AddCitizenVerifier(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddHttpClient<IStatusListCache, StatusListCache>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IVerifierSessionStore, InMemoryVerifierSessionStore>();
        services.AddSingleton<IPresentationRequestBuilder, PresentationRequestBuilder>();

        // Feature 120 — DID resolver infrastructure (cache, OTel meter, registry resolvers).
        services.AddDidResolvers(configuration);

        // Feature 120 US1 — production issuer key resolver. Composite tries DID-backed first
        // then falls back to the JWK registry (empty in production; demo-mint populates it
        // in dev). DID-resolver path enforces the FR-003 failure-mode classification; the
        // registry tier exists only to keep dev/demo flows working without loosening
        // production behaviour.
        services.AddSingleton<JwkRegistryIssuerKeyResolver>();
        services.AddSingleton<DidResolverBackedIssuerKeyResolver>();
        services.AddSingleton<IIssuerKeyResolver>(sp => new CompositeIssuerKeyResolver(
        [
            sp.GetRequiredService<DidResolverBackedIssuerKeyResolver>(),
            sp.GetRequiredService<JwkRegistryIssuerKeyResolver>()
        ]));

        // Feature 120 T022 — production default is REQUIRED (FR-019 / D5).
        var requireIssuerSignature = configuration?
            .GetValue<bool?>("IssuerSignature:Required")
            ?? configuration?.GetValue<bool?>("Verifier:RequireIssuerSignature")
            ?? true;
        services.AddSingleton<IVerifiablePresentationValidator>(sp =>
            new VerifiablePresentationValidator(
                sp.GetRequiredService<IStatusListCache>(),
                sp.GetRequiredService<IIssuerKeyResolver>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<VerifiablePresentationValidator>>(),
                requireIssuerSignature));

        services.AddSingleton<QrRenderer>();
        return services;
    }
}
