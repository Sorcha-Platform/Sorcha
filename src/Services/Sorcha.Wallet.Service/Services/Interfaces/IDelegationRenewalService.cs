// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Renews a citizen wallet's device delegation credential (Feature 114, T106).
/// Composes Tenant Service device lookup + <see cref="IHolderKeyService"/> +
/// <see cref="IDeviceDelegationIssuer"/> + Tenant Service device record refresh
/// into one idempotent call. Wallets call this when their current delegation
/// is approaching expiry (within 30 days, per the v1 contract).
/// </summary>
public interface IDelegationRenewalService
{
    /// <summary>
    /// Re-issues the delegation credential bound to the device, signed by the
    /// citizen's holder key. Refreshes the Tenant <see cref="Sorcha.Tenant.Service.Models.PlatformUserDevice"/>
    /// row's delegation expiry + JTI in the same call.
    /// </summary>
    /// <returns>
    /// A renewal result, or null when the device does not exist or is not owned
    /// by the supplied platform user.
    /// </returns>
    Task<DelegationRenewalResponse?> RenewAsync(
        Guid deviceId,
        Guid platformUserId,
        string citizenWalletAddress,
        Guid organizationId,
        CancellationToken ct = default);
}
