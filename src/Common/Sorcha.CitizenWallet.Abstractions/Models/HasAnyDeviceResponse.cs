// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>GET /api/v1/me/devices/has-any</c>. Aggregate read
/// used by F128 cold-start surfaces (wallet PWA pairing takeover trigger,
/// Sorcha Web nag-banner trigger) — does not leak the per-device list.
/// </summary>
public sealed record HasAnyDeviceResponse
{
    /// <summary>
    /// <c>true</c> when the calling citizen has at least one
    /// ACTIVE (non-revoked) paired device.
    /// </summary>
    public bool HasAnyDevice { get; init; }

    /// <summary>
    /// Enrolment timestamp of the most recent active device, or <c>null</c>
    /// when <see cref="HasAnyDevice"/> is <c>false</c>.
    /// </summary>
    public DateTimeOffset? LatestEnrolledAt { get; init; }
}
