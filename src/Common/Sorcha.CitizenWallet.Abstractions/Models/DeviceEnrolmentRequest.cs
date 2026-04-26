// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Request body for <c>POST /api/v1/wallet/devices/enrol</c>. Submitted by the
/// citizen wallet PWA after the citizen authenticates and the wallet generates
/// its non-extractable device keypair.
/// </summary>
public sealed record DeviceEnrolmentRequest
{
    /// <summary>Citizen-editable label, 1..120 chars.</summary>
    public string DeviceLabel { get; init; } = string.Empty;

    /// <summary>Public half of the device's ECDSA P-256 signing keypair.</summary>
    public EcP256PublicJwk DevicePublicJwk { get; init; } = new();

    /// <summary>Free-form platform descriptor, e.g. <c>"iOS 19 / Safari 19"</c>. ≤120 chars.</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Raw user-agent at enrolment time, for audit. ≤512 chars.</summary>
    public string UserAgent { get; init; } = string.Empty;
}
