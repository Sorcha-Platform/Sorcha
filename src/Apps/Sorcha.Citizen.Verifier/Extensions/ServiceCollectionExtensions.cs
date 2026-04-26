// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Citizen.Verifier.Services;

namespace Sorcha.Citizen.Verifier.Extensions;

/// <summary>
/// DI extensions for the Sorcha citizen reference verifier (Feature 114, T073).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the verifier's services. Status list cache uses a typed HttpClient
    /// so production deployments can plug in resilience handlers (Polly, etc.) without
    /// touching this code.
    /// </summary>
    public static IServiceCollection AddCitizenVerifier(this IServiceCollection services)
    {
        services.AddHttpClient<IStatusListCache, StatusListCache>();
        return services;
    }
}
