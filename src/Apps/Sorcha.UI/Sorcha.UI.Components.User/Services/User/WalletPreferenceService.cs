// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Wallet;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages the user's default wallet preference using server-side storage
/// via IUserPreferencesService, with one-time migration from browser localStorage.
/// </summary>
public class WalletPreferenceService : IWalletPreferenceService
{
    private const string LegacyStorageKey = "sorcha:preferences:defaultWallet";
    private readonly IUserPreferencesService _userPreferences;
    private readonly ILocalStorageService _localStorage;
    private readonly ILogger<WalletPreferenceService> _logger;
    private bool _migrationChecked;

    public WalletPreferenceService(
        IUserPreferencesService userPreferences,
        ILocalStorageService localStorage,
        ILogger<WalletPreferenceService> logger)
    {
        _userPreferences = userPreferences;
        _localStorage = localStorage;
        _logger = logger;
    }

    public async Task<string?> GetDefaultWalletAsync()
    {
        await MigrateLegacyPreferenceAsync();

        try
        {
            return await _userPreferences.GetDefaultWalletAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get default wallet from server");
            return null;
        }
    }

    public async Task SetDefaultWalletAsync(string walletAddress)
    {
        await _userPreferences.SetDefaultWalletAsync(walletAddress);
    }

    public async Task ClearDefaultWalletAsync()
    {
        await _userPreferences.ClearDefaultWalletAsync();
    }

    public async Task<string?> GetSmartDefaultAsync(List<WalletDto> wallets)
    {
        if (wallets is not { Count: > 0 })
            return null;

        // Single wallet: auto-select
        if (wallets.Count == 1)
            return wallets[0].Address;

        // Multiple wallets: check stored preference
        var storedDefault = await GetDefaultWalletAsync();
        if (!string.IsNullOrEmpty(storedDefault))
        {
            // Verify the stored default is still in the wallet list
            if (wallets.Any(w => w.Address == storedDefault))
                return storedDefault;

            // Stored default no longer valid — clear it
            await ClearDefaultWalletAsync();
        }

        // Fallback: first wallet
        return wallets[0].Address;
    }

    /// <summary>
    /// One-time migration: if the legacy localStorage key exists, migrate
    /// its value to the server-side preference store and remove it.
    /// </summary>
    private async Task MigrateLegacyPreferenceAsync()
    {
        if (_migrationChecked)
            return;

        _migrationChecked = true;

        try
        {
            var legacyValue = await _localStorage.GetItemAsStringAsync(LegacyStorageKey);
            if (!string.IsNullOrEmpty(legacyValue))
            {
                _logger.LogInformation("Migrating default wallet preference from localStorage to server");
                await _userPreferences.SetDefaultWalletAsync(legacyValue);
                await _localStorage.RemoveItemAsync(LegacyStorageKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to migrate legacy wallet preference from localStorage");
            // Non-fatal — the user can set a new default manually
        }
    }
}
