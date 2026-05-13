// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Wallet;

namespace Sorcha.UI.Core.Services.Wallet;

/// <summary>
/// Service interface for interacting with the Wallet API
/// </summary>
public interface IWalletApiService
{
    /// <summary>
    /// Get all wallets for the current user
    /// </summary>
    Task<List<WalletDto>> GetWalletsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get a wallet by address
    /// </summary>
    Task<WalletDto?> GetWalletAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Create a new wallet
    /// </summary>
    Task<CreateWalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct = default);

    /// <summary>
    /// Recover a wallet from mnemonic phrase
    /// </summary>
    Task<WalletDto> RecoverWalletAsync(RecoverWalletRequest request, CancellationToken ct = default);

    /// <summary>
    /// Delete a wallet (soft delete)
    /// </summary>
    Task<bool> DeleteWalletAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Sign data with a wallet's private key
    /// </summary>
    Task<SignTransactionResponse> SignDataAsync(string address, SignTransactionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get addresses for a wallet
    /// </summary>
    Task<AddressListResponse> GetAddressesAsync(string address, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>
    /// Register a client-derived address with a wallet
    /// </summary>
    Task<WalletAddressDto> RegisterAddressAsync(string address, RegisterAddressRequest request, CancellationToken ct = default);
}
