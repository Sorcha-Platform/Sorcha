// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Status of an enrolled citizen wallet device. Once revoked, a device never returns
/// to active — the citizen must enrol a new device.
/// </summary>
public enum PlatformUserDeviceStatus
{
    /// <summary>Device is enrolled and may make presentations on the holder's behalf.</summary>
    Active = 0,

    /// <summary>Device has been revoked; status-list bit is set.</summary>
    Revoked = 1
}

/// <summary>
/// Server-side record that a <see cref="PlatformUser"/> has enrolled the citizen wallet
/// PWA on a specific device (Feature 114). One row per (PlatformUser, device key) pair.
/// </summary>
/// <remarks>
/// The Tenant Service owns this entity because it lives alongside the platform identity
/// (PlatformUser is the recovery anchor — see Feature 112). The Wallet Service consults
/// it via service-to-service auth when issuing or renewing device delegation credentials,
/// flipping status-list bits, and enforcing per-device authorisation.
/// Cascade delete from PlatformUser ensures no orphan devices.
/// </remarks>
public class PlatformUserDevice
{
    /// <summary>Unique device identifier, also used as the citizen-visible <c>deviceId</c>.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning platform user.</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Citizen-editable label, 1..120 chars (e.g. "Stuart's iPhone 16").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>SHA-256 thumbprint of the canonical device public JWK (43 base64url chars).</summary>
    public string DevicePublicJwkThumbprint { get; set; } = string.Empty;

    /// <summary>Canonical JWK JSON for the device public key (≤512 chars).</summary>
    public string DevicePublicJwkJson { get; set; } = string.Empty;

    /// <summary>Free-form platform descriptor at enrolment, e.g. "iOS 19 / Safari 19".</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Raw User-Agent header at enrolment, retained for audit.</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Active or Revoked.</summary>
    public PlatformUserDeviceStatus Status { get; set; } = PlatformUserDeviceStatus.Active;

    /// <summary>UTC time of original enrolment (preserved across delegation renewals).</summary>
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time of revocation, if revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// PlatformUser who triggered the revocation. Usually the device's owner self-revoking;
    /// may be a support principal in future when a help-desk-mediated path is introduced.
    /// </summary>
    public Guid? RevokedByPlatformUserId { get; set; }

    /// <summary>UTC time of the most recent successful sync from this device.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>UTC expiry of the current device delegation credential.</summary>
    public DateTimeOffset DelegationExpiresAt { get; set; }

    /// <summary>JTI of the current delegation credential (rotates on renewal).</summary>
    public string DelegationCredentialJti { get; set; } = string.Empty;

    /// <summary>Bit position allocated in the org's citizen-devices status list.</summary>
    public int StatusListIndex { get; set; }

    /// <summary>Navigation property to the owning PlatformUser.</summary>
    public PlatformUser PlatformUser { get; set; } = default!;
}
