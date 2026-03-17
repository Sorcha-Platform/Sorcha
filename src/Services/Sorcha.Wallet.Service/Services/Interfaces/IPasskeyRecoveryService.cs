// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Handles passkey-bound wallet recovery (Path 4).
/// </summary>
public interface IPasskeyRecoveryService
{
    /// <summary>
    /// Recovers all wallets for a user using their passkey.
    /// </summary>
    /// <param name="userId">Authenticated user ID.</param>
    /// <param name="tenantId">User's tenant/org ID.</param>
    /// <param name="passkeyCredentialId">Passkey credential ID used for authentication.</param>
    /// <param name="ipAddress">Client IP for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recovery result with restored wallets and delegation status.</returns>
    Task<RecoveryResult> RecoverAsync(
        string userId,
        string tenantId,
        string passkeyCredentialId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
