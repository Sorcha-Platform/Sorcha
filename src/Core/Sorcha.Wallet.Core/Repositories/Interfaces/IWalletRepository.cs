// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Core.Domain.Entities;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Core.Repositories.Interfaces;

/// <summary>
/// Repository for wallet data persistence
/// </summary>
public interface IWalletRepository
{
    /// <summary>
    /// Adds a new wallet
    /// </summary>
    /// <param name="wallet">Wallet to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAsync(WalletEntity wallet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing wallet
    /// </summary>
    /// <param name="wallet">Wallet to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAsync(WalletEntity wallet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a wallet
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a wallet by address
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="includeAddresses">Include derived addresses</param>
    /// <param name="includeDelegates">Include access grants</param>
    /// <param name="includeTransactions">Include transactions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Wallet if found</returns>
    Task<WalletEntity?> GetByAddressAsync(
        string address,
        bool includeAddresses = false,
        bool includeDelegates = false,
        bool includeTransactions = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all wallets for an owner
    /// </summary>
    /// <param name="owner">Owner subject identifier</param>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of wallets</returns>
    Task<IEnumerable<WalletEntity>> GetByOwnerAsync(
        string owner,
        string tenant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets wallets by tenant
    /// </summary>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of wallets</returns>
    Task<IEnumerable<WalletEntity>> GetByTenantAsync(
        string tenant,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all wallets across all tenants with paging.
    /// Used for system-level operations like bloom filter rebuild.
    /// </summary>
    /// <param name="skip">Number of records to skip</param>
    /// <param name="take">Number of records to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of wallets</returns>
    Task<IEnumerable<WalletEntity>> GetAllPagedAsync(
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a wallet exists
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if wallet exists</returns>
    Task<bool> ExistsAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a derived address to a wallet
    /// </summary>
    /// <param name="walletAddress">Parent wallet address</param>
    /// <param name="address">Address to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAddressAsync(
        string walletAddress,
        WalletAddress address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all addresses for a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of addresses</returns>
    Task<IEnumerable<WalletAddress>> GetAddressesAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an access grant to a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="access">Access grant to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddAccessAsync(
        string walletAddress,
        WalletAccess access,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an access grant
    /// </summary>
    /// <param name="access">Access grant to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAccessAsync(WalletAccess access, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific access grant by its unique identifier
    /// </summary>
    /// <param name="accessId">Access grant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Access grant if found, null otherwise</returns>
    Task<WalletAccess?> GetAccessByIdAsync(Guid accessId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all access grants for a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="activeOnly">Only return active grants</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of access grants</returns>
    Task<IEnumerable<WalletAccess>> GetAccessAsync(
        string walletAddress,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves all changes
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
