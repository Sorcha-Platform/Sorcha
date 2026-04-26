// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Request body for <c>POST /api/v1/wallet/devices/renew-delegation</c>. Idempotent;
/// the wallet calls this when its current delegation is within 30 days of expiry.
/// </summary>
public sealed record DelegationRenewalRequest
{
    /// <summary>The device whose delegation should be renewed.</summary>
    public Guid DeviceId { get; init; }
}
