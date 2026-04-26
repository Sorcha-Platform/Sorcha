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
        services.AddSingleton<IVerifiablePresentationValidator, VerifiablePresentationValidator>();
        services.AddSingleton<QrRenderer>();
        return services;
    }
}
