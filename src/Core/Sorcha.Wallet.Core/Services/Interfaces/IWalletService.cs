// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Core.Domain.Entities;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;
using Sorcha.Wallet.Core.Domain.ValueObjects;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Main facade for wallet operations
/// </summary>
public interface IWalletService
{
    /// <summary>
    /// Creates a new wallet with a randomly generated mnemonic
    /// </summary>
    /// <param name="name">Wallet name</param>
    /// <param name="algorithm">Cryptographic algorithm (ED25519, SECP256K1, RSA)</param>
    /// <param name="owner">Owner subject identifier</param>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="wordCount">Mnemonic word count (12 or 24)</param>
    /// <param name="passphrase">Optional BIP39 passphrase</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created wallet and the mnemonic (MUST be saved by caller)</returns>
    Task<(WalletEntity Wallet, Mnemonic Mnemonic)> CreateWalletAsync(
        string name,
        string algorithm,
        string owner,
        string tenant,
        int wordCount = 12,
        string? passphrase = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers a wallet from an existing mnemonic
    /// </summary>
    /// <param name="mnemonic">BIP39 mnemonic phrase</param>
    /// <param name="name">Wallet name</param>
    /// <param name="algorithm">Cryptographic algorithm</param>
    /// <param name="owner">Owner subject identifier</param>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="passphrase">Optional BIP39 passphrase</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recovered wallet</returns>
    Task<WalletEntity> RecoverWalletAsync(
        Mnemonic mnemonic,
        string name,
        string algorithm,
        string owner,
        string tenant,
        string? passphrase = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a wallet by address
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Wallet if found</returns>
    Task<WalletEntity?> GetWalletAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all wallets for a given owner
    /// </summary>
    /// <param name="owner">Owner subject identifier</param>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of wallets</returns>
    Task<IEnumerable<WalletEntity>> GetWalletsByOwnerAsync(
        string owner,
        string tenant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates wallet metadata
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="name">New name</param>
    /// <param name="description">New description</param>
    /// <param name="tags">New tags</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated wallet</returns>
    Task<WalletEntity> UpdateWalletAsync(
        string address,
        string? name = null,
        string? description = null,
        Dictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a wallet (soft delete)
    /// </summary>
    /// <param name="address">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteWalletAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new derived address for the wallet
    /// </summary>
    /// <param name="walletAddress">Parent wallet address</param>
    /// <param name="index">Address index</param>
    /// <param name="isChange">Is this a change address?</param>
    /// <param name="label">Optional label</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Generated address</returns>
    Task<WalletAddress> GenerateAddressAsync(
        string walletAddress,
        int index,
        bool isChange = false,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a transaction with the wallet's private key
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="transactionData">Transaction data to sign</param>
    /// <param name="derivationPath">Optional BIP44 derivation path or Sorcha system path (e.g., "sorcha:register-attestation")</param>
    /// <param name="isPreHashed">When true, transactionData is already hashed and should be signed directly without additional SHA-256</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple containing the signature and the public key used for signing (derived or master)</returns>
    Task<(byte[] Signature, byte[] PublicKey)> SignTransactionAsync(
        string walletAddress,
        byte[] transactionData,
        string? derivationPath = null,
        bool isPreHashed = false,
        CancellationToken cancellationToken = default);
}
