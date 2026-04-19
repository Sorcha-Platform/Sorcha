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

    /// <summary>
    /// Announces a newly created local wallet address to every register's bloom filter.
    /// Called from wallet-create / address-derive / org-key-derive paths so the
    /// Register Service's InboundTransactionRouter can match local recipients on
    /// docket-sealed transactions.
    /// </summary>
    /// <remarks>
    /// This method swallows all failures and logs them as warnings; bloom registration
    /// is a best-effort cache update and must not fail the caller's wallet-create
    /// operation. The Register Service's startup-rebuild hosted service reconciles
    /// any addresses missed here by iterating <c>IWalletRepository.GetAllPagedAsync</c>.
    /// Policy is <b>Option B</b>: the address is added to every register discovered
    /// via <see cref="Sorcha.ServiceClients.Register.IRegisterServiceClient.GetInternalRegistersAsync"/>.
    /// </remarks>
    /// <param name="address">The newly created wallet address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyLocalAddressCreatedAsync(string address, CancellationToken cancellationToken = default);
}
