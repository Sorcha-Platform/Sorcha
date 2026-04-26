// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Status of an enrolled wallet device.
/// </summary>
public enum DeviceStatus
{
    /// <summary>Device is enrolled and may make presentations.</summary>
    Active = 0,

    /// <summary>Device has been revoked. Terminal — citizen must enrol a new device.</summary>
    Revoked = 1
}

/// <summary>
/// Citizen-visible summary of an enrolled wallet device. Returned by
/// <c>GET /api/v1/wallet/devices</c> and <c>GET /api/v1/me/devices</c>.
/// </summary>
public sealed record DeviceSummary
{
    /// <summary>Server-assigned device identifier.</summary>
    public Guid DeviceId { get; init; }

    /// <summary>Citizen-set label.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Free-form platform descriptor at enrolment.</summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>Active or Revoked.</summary>
    public DeviceStatus Status { get; init; }

    /// <summary>UTC time of original enrolment.</summary>
    public DateTimeOffset EnrolledAt { get; init; }

    /// <summary>UTC time of revocation, if revoked.</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>UTC time of the most recent successful sync (or null if never).</summary>
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>UTC time at which the current delegation credential expires.</summary>
    public DateTimeOffset DelegationExpiresAt { get; init; }
}
