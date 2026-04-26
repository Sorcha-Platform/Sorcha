// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.Citizen.Verifier.Services;

namespace Sorcha.Citizen.Verifier.Extensions;

/// <summary>
/// DI extensions for the Sorcha citizen reference verifier (Feature 114).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers verifier services — status list cache (T072), presentation request
    /// builder (T088), session store, and the VP validator (T089). The status list
    /// cache uses a typed HttpClient so production deployments can plug in resilience
    /// handlers (Polly, etc.) without touching this code.
    /// </summary>
    public static IServiceCollection AddCitizenVerifier(this IServiceCollection services)
    {
        services.AddHttpClient<IStatusListCache, StatusListCache>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IVerifierSessionStore, InMemoryVerifierSessionStore>();
        services.AddSingleton<IPresentationRequestBuilder, PresentationRequestBuilder>();
        // Issuer key resolver: in-memory JWK registry is the v1 default so the
        // demo-mint endpoint can register the per-mint issuer key and the validator
        // can verify the credential JWT signature end-to-end. Production swaps this
        // for a DID-based resolver that reads tenant register verification methods.
        services.AddSingleton<JwkRegistryIssuerKeyResolver>();
        services.AddSingleton<IIssuerKeyResolver>(sp =>
            sp.GetRequiredService<JwkRegistryIssuerKeyResolver>());
        services.AddSingleton<IVerifiablePresentationValidator, VerifiablePresentationValidator>();
        services.AddSingleton<QrRenderer>();
        return services;
    }
}
