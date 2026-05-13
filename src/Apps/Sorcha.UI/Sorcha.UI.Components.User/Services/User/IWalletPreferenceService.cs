// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Wallet;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Manages the user's default wallet preference for signing operations.
/// </summary>
public interface IWalletPreferenceService
{
    /// <summary>
    /// Gets the stored default wallet address from local storage.
    /// </summary>
    Task<string?> GetDefaultWalletAsync();

    /// <summary>
    /// Stores the user's preferred default wallet address.
    /// </summary>
    Task SetDefaultWalletAsync(string walletAddress);

    /// <summary>
    /// Clears the stored default wallet preference.
    /// </summary>
    Task ClearDefaultWalletAsync();

    /// <summary>
    /// Resolves the smart default wallet: auto-selects if single wallet,
    /// uses stored preference if multiple, falls back to first wallet.
    /// Returns null if no wallets are available.
    /// </summary>
    Task<string?> GetSmartDefaultAsync(List<WalletDto> wallets);
}
