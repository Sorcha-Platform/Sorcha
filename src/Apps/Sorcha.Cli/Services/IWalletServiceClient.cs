// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Refit;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for the Wallet Service API.
/// </summary>
public interface IWalletServiceClient
{
    /// <summary>
    /// Creates a new wallet.
    /// </summary>
    [Post("/api/v1/wallets")]
    Task<CreateWalletResponse> CreateWalletAsync(
        [Body] CreateWalletRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Recovers a wallet from mnemonic phrase.
    /// </summary>
    [Post("/api/v1/wallets/recover")]
    Task<Wallet> RecoverWalletAsync(
        [Body] RecoverWalletRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists all wallets for the current user.
    /// </summary>
    [Get("/api/v1/wallets")]
    Task<List<Wallet>> ListWalletsAsync(
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a wallet by address.
    /// </summary>
    [Get("/api/v1/wallets/{address}")]
    Task<Wallet> GetWalletAsync(
        string address,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates wallet metadata.
    /// </summary>
    [Patch("/api/v1/wallets/{address}")]
    Task<Wallet> UpdateWalletAsync(
        string address,
        [Body] UpdateWalletRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Deletes a wallet (soft delete).
    /// </summary>
    [Delete("/api/v1/wallets/{address}")]
    Task DeleteWalletAsync(
        string address,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Signs a transaction with a wallet's private key.
    /// </summary>
    [Post("/api/v1/wallets/{address}/sign")]
    Task<SignTransactionResponse> SignTransactionAsync(
        string address,
        [Body] SignTransactionRequest request,
        [Header("Authorization")] string authorization);

    // --- Access Delegation ---

    /// <summary>
    /// Grants access to a wallet for a subject.
    /// </summary>
    [Post("/api/v1/wallets/{walletAddress}/access")]
    Task<WalletAccessGrant> GrantAccessAsync(
        string walletAddress,
        [Body] GrantAccessRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists active access grants for a wallet.
    /// </summary>
    [Get("/api/v1/wallets/{walletAddress}/access")]
    Task<List<WalletAccessGrant>> ListAccessAsync(
        string walletAddress,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Revokes access for a subject from a wallet.
    /// </summary>
    [Delete("/api/v1/wallets/{walletAddress}/access/{subject}")]
    Task RevokeAccessAsync(
        string walletAddress,
        string subject,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Checks if a subject has the required access right on a wallet.
    /// </summary>
    [Get("/api/v1/wallets/{walletAddress}/access/{subject}/check")]
    Task<AccessCheckResponse> CheckAccessAsync(
        string walletAddress,
        string subject,
        [Query] string requiredRight,
        [Header("Authorization")] string authorization);
}
