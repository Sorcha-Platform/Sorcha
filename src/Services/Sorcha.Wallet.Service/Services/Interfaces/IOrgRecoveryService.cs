// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Handles organization-managed wallet recovery (Path 3).
/// </summary>
public interface IOrgRecoveryService
{
    /// <summary>
    /// Recovers all wallets for a target user initiated by an org admin.
    /// </summary>
    Task<RecoveryResult> RecoverAsync(
        string adminUserId,
        string targetUserId,
        string tenantId,
        string orgRecoveryKeySignature,
        bool skipDelegationRevocation = false,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
