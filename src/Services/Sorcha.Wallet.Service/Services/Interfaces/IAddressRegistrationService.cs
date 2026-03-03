// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Orchestrates wallet address registration with the Register Service bloom filter.
/// Called on wallet create/delete to keep the local address index in sync.
/// </summary>
public interface IAddressRegistrationService
{
    /// <summary>
    /// Register a wallet address as local in the Register Service bloom filter.
    /// Called after a wallet is successfully created or a new address is derived.
    /// </summary>
    /// <param name="address">The wallet address to register.</param>
    /// <param name="registerId">The register this address belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration succeeded.</returns>
    Task<bool> RegisterAddressAsync(string address, string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a wallet address from the Register Service bloom filter.
    /// Called when a wallet is deleted or deactivated. Triggers a full bloom filter rebuild
    /// because bloom filters don't support individual deletion.
    /// </summary>
    /// <param name="address">The wallet address to remove.</param>
    /// <param name="registerId">The register this address belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removal/rebuild was triggered.</returns>
    Task<bool> RemoveAddressAsync(string address, string registerId, CancellationToken cancellationToken = default);
}
