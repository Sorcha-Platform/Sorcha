// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing wallet access delegation.
/// </summary>
public interface IWalletAccessService
{
    /// <summary>
    /// Grants access to a wallet for a subject.
    /// </summary>
    Task<WalletAccessGrantViewModel?> GrantAccessAsync(
        string walletAddress, GrantAccessFormModel form, CancellationToken ct = default);

    /// <summary>
    /// Lists all active access grants for a wallet.
    /// </summary>
    Task<List<WalletAccessGrantViewModel>> ListAccessAsync(
        string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Revokes access for a subject from a wallet.
    /// </summary>
    Task<bool> RevokeAccessAsync(
        string walletAddress, string subject, CancellationToken ct = default);

    /// <summary>
    /// Checks if a subject has the required access right on a wallet.
    /// </summary>
    Task<AccessCheckResult?> CheckAccessAsync(
        string walletAddress, string subject, string requiredRight, CancellationToken ct = default);
}
