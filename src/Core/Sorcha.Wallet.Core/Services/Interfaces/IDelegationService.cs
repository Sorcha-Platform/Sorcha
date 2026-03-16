// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// Service for managing wallet access control and delegation
/// </summary>
public interface IDelegationService
{
    /// <summary>
    /// Grants access to a wallet for a subject
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="subject">Subject identifier</param>
    /// <param name="accessRight">Type of access to grant</param>
    /// <param name="grantedBy">Subject granting the access</param>
    /// <param name="reason">Reason for granting access</param>
    /// <param name="expiresAt">Optional expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created access entry</returns>
    Task<WalletAccess> GrantAccessAsync(
        string walletAddress,
        string subject,
        AccessRight accessRight,
        string grantedBy,
        string? reason = null,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes access to a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="subject">Subject whose access to revoke</param>
    /// <param name="revokedBy">Subject revoking the access</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RevokeAccessAsync(
        string walletAddress,
        string subject,
        string revokedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active access grants for a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active access grants</returns>
    Task<IEnumerable<WalletAccess>> GetActiveAccessAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a subject has access to a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="subject">Subject identifier</param>
    /// <param name="requiredRight">Minimum required access right</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if subject has access</returns>
    Task<bool> HasAccessAsync(
        string walletAddress,
        string subject,
        AccessRight requiredRight,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing access grant
    /// </summary>
    /// <param name="accessId">Access entry ID</param>
    /// <param name="accessRight">New access right</param>
    /// <param name="expiresAt">New expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated access entry</returns>
    Task<WalletAccess> UpdateAccessAsync(
        Guid accessId,
        AccessRight? accessRight = null,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default);
}
