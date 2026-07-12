// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Refit;
using Sorcha.Cli.Models;
using Sorcha.ServiceClients.Wallet;

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
    Task<WalletDto> RecoverWalletAsync(
        [Body] RecoverWalletRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Recovers a system wallet (validator identity) from a BIP39 mnemonic.
    /// Used to seat the genesis-ceremony validator on a node so the Validator
    /// Service can sign system register dockets.
    /// </summary>
    [Post("/api/v1/wallets/system/recover")]
    Task<RecoverSystemWalletResponse> RecoverSystemWalletAsync(
        [Body] RecoverSystemWalletApiRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists all wallets for the current user.
    /// </summary>
    [Get("/api/v1/wallets")]
    Task<List<WalletDto>> ListWalletsAsync(
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a wallet by address.
    /// </summary>
    [Get("/api/v1/wallets/{address}")]
    Task<WalletDto> GetWalletAsync(
        string address,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates wallet metadata.
    /// </summary>
    [Patch("/api/v1/wallets/{address}")]
    Task<WalletDto> UpdateWalletAsync(
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

    // --- Organisation Key Derivation (Feature 083) ---
    // Response DTOs are reused from Sorcha.ServiceClients.Wallet (no duplication).
    // These methods use the CLI's user bearer token, unlike the shared
    // WalletServiceClient which authenticates as a service principal.

    /// <summary>
    /// Provisions an organisation HD master key (one-shot; returns the mnemonic once).
    /// </summary>
    [Post("/api/wallets/org/{orgId}/master-key")]
    Task<OrgMasterKeyProvisionResponse> ProvisionOrgMasterKeyAsync(
        string orgId,
        [Body] ProvisionOrgMasterKeyRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Derives a per-user key from an organisation's master key.
    /// </summary>
    [Post("/api/wallets/org/{orgId}/derive-key")]
    Task<DerivedKeyResponse> DeriveOrgKeyAsync(
        string orgId,
        [Body] DeriveOrgKeyRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Rotates a derived organisation key (new key at next index; old becomes decrypt-only).
    /// </summary>
    [Post("/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate")]
    Task<DerivedKeyResponse> RotateOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Revokes a derived organisation key (wallet locked; DID revocation for identity keys).
    /// </summary>
    [Delete("/api/wallets/org/{orgId}/keys/{derivedKeyId}")]
    Task<RevokeKeyResponse> RevokeOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        [Header("Authorization")] string authorization);
}

/// <summary>
/// Request body for provisioning an organisation master key.
/// </summary>
public class ProvisionOrgMasterKeyRequest
{
    public string Algorithm { get; set; } = "ED25519";
}

/// <summary>
/// Request body for deriving an organisation user key.
/// </summary>
public class DeriveOrgKeyRequest
{
    public string UserId { get; set; } = string.Empty;
    public uint DepartmentId { get; set; }
    public string KeyUsage { get; set; } = string.Empty;
}
