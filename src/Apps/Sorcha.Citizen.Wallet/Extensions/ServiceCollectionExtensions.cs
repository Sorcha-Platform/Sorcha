// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sorcha.Citizen.Wallet.Services;
using Sorcha.Citizen.Wallet.Services.Presentation;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Citizen.Wallet.Extensions;

/// <summary>DI extensions for the citizen wallet PWA (Feature 114, T065).</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the PWA's services as singletons (single-user-per-tab).
    /// Production wiring uses WebCrypto for device keys and IndexedDB for the
    /// credential cache, delegation store, and status list cache — all backed
    /// by <c>indexeddb-bridge.js</c>. The in-memory variants are kept for unit
    /// tests where IJSRuntime isn't available.
    /// </summary>
    public static IServiceCollection AddCitizenWalletServices(
        this IServiceCollection services, string gatewayBaseAddress)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPresentationEngine, PresentationEngine>();
        services.AddSingleton<IDeviceKeyService, WebCryptoDeviceKeyService>();
        services.AddSingleton<ICredentialCache, IndexedDbCredentialCache>();
        services.AddSingleton<IDelegationStore, IndexedDbDelegationStore>();
        services.AddSingleton<IStatusListService, IndexedDbStatusListService>();
        services.AddSingleton<ISyncCursorStore, IndexedDbSyncCursorStore>();
        services.AddSingleton<IAccessTokenStore, IndexedDbAccessTokenStore>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IDeviceMetaStore, IndexedDbDeviceMetaStore>();
        services.AddSingleton<IEnrolmentService, EnrolmentService>();
        services.AddSingleton<IDelegationRenewalClient, DelegationRenewalClient>();

        // Auth surface: a separate HttpClient that does NOT inject the bearer
        // token (so sign-in requests don't carry stale tokens).
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient<IAuthService, AuthService>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress));

        // Citizen wallet client: every outbound call automatically carries the
        // wallet's stored bearer token.
        services.AddHttpClient<ICitizenWalletClient, CitizenWalletClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<BearerTokenHandler>();

        return services;
    }
}
