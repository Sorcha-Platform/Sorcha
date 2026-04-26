// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>POST /api/v1/wallet/devices/renew-delegation</c>.
/// </summary>
public sealed record DelegationRenewalResponse
{
    /// <summary>The renewed (or existing-and-not-yet-due) compact SD-JWT VC.</summary>
    public string DelegationCredential { get; init; } = string.Empty;

    /// <summary>UTC time at which the returned delegation expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
