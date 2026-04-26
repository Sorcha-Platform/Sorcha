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
    /// IDeviceKeyService is wired to <see cref="WebCryptoDeviceKeyService"/> by
    /// default — the InMemory variant is reserved for unit tests where IJSRuntime
    /// isn't available. Cache + delegation + status-list start as in-memory MVP
    /// impls; IndexedDB-backed replacements land with T060-T062.
    /// </summary>
    public static IServiceCollection AddCitizenWalletServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPresentationEngine, PresentationEngine>();
        services.AddSingleton<IDeviceKeyService, WebCryptoDeviceKeyService>();
        services.AddSingleton<ICredentialCache, InMemoryCredentialCache>();
        services.AddSingleton<IDelegationStore, InMemoryDelegationStore>();
        services.AddSingleton<IStatusListService, NoopStatusListService>();
        return services;
    }
}
