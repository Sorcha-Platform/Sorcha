// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Service for org-level HD key derivation. Provisions master keys,
/// derives user keys, rotates, and revokes.
/// </summary>
public interface IOrgKeyDerivationService
{
    /// <summary>
    /// Provision org master key. Returns mnemonic ONCE for admin backup.
    /// </summary>
    /// <param name="organizationId">Organisation identifier</param>
    /// <param name="algorithm">Cryptographic algorithm (default ED25519)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Provisioning result including the one-time mnemonic</returns>
    Task<OrgMasterKeyProvisionResult> ProvisionMasterKeyAsync(
        string organizationId, string algorithm = "ED25519", CancellationToken ct = default);

    /// <summary>
    /// Derive user key at path. Idempotent — returns existing if path already derived.
    /// </summary>
    /// <param name="organizationId">Organisation identifier</param>
    /// <param name="userId">User identifier</param>
    /// <param name="departmentId">Department index in the derivation path</param>
    /// <param name="usage">Intended key usage</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Derived key result with wallet address and derivation metadata</returns>
    Task<DerivedKeyResult> DeriveUserKeyAsync(
        string organizationId, string userId, uint departmentId, KeyUsage usage, CancellationToken ct = default);

    /// <summary>
    /// Rotate key — derives at next index, marks old as Rotated.
    /// </summary>
    /// <param name="derivedKeyRecordId">Existing derived key record identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>New derived key result replacing the rotated key</returns>
    Task<DerivedKeyResult> RotateKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default);

    /// <summary>
    /// Revoke key — marks as Revoked, locks wallet.
    /// </summary>
    /// <param name="derivedKeyRecordId">Derived key record identifier to revoke</param>
    /// <param name="ct">Cancellation token</param>
    Task RevokeKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default);
}

/// <summary>
/// Result of provisioning an org master key.
/// </summary>
/// <param name="OrganizationId">Organisation identifier</param>
/// <param name="MasterPublicKey">Master public key (base64)</param>
/// <param name="Mnemonic">BIP39 mnemonic — returned once only, caller must persist</param>
/// <param name="Algorithm">Cryptographic algorithm used</param>
public record OrgMasterKeyProvisionResult(
    string OrganizationId,
    string MasterPublicKey,
    string Mnemonic,
    string Algorithm);

/// <summary>
/// Result of deriving or rotating a key.
/// </summary>
/// <param name="DerivedKeyId">Unique identifier for the derived key record</param>
/// <param name="WalletAddress">Wallet address of the derived key</param>
/// <param name="DerivationPath">BIP44-style derivation path</param>
/// <param name="KeyUsage">Intended key usage</param>
/// <param name="KeyIndex">Index within the derivation path</param>
/// <param name="Status">Key status (Active, Rotated, Revoked)</param>
/// <param name="CustodyMode">Custody mode (Platform, Hybrid)</param>
/// <param name="CreatedAt">Timestamp when the key was created</param>
public record DerivedKeyResult(
    Guid DerivedKeyId,
    string WalletAddress,
    string DerivationPath,
    KeyUsage KeyUsage,
    uint KeyIndex,
    string Status,
    string CustodyMode,
    DateTime CreatedAt);
