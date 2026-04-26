// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Citizen.Wallet.Services;
using Sorcha.Citizen.Wallet.Services.Presentation;

namespace Sorcha.Citizen.Wallet.Extensions;

/// <summary>DI extensions for the citizen wallet PWA (Feature 114, T065).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the PWA's services as singletons (single-user-per-tab).
    /// v1 MVP wires in-memory implementations; the production WebCrypto +
    /// IndexedDB-backed bridge implementations land with T054-T067 hardening.
    /// </summary>
    public static IServiceCollection AddCitizenWalletServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPresentationEngine, PresentationEngine>();
        services.AddSingleton<IDeviceKeyService, InMemoryDeviceKeyService>();
        services.AddSingleton<ICredentialCache, InMemoryCredentialCache>();
        services.AddSingleton<IDelegationStore, InMemoryDelegationStore>();
        services.AddSingleton<IStatusListService, NoopStatusListService>();
        return services;
    }
}
