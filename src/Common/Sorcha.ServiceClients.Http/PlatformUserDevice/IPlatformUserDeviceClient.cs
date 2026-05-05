// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.PlatformUserDevice;

/// <summary>
/// Service-to-service client for citizen wallet device registration on the Tenant
/// Service (Feature 114). Used by Wallet Service from its enrolment endpoint after
/// issuing a device delegation credential.
/// </summary>
public interface IPlatformUserDeviceClient
{
    /// <summary>
    /// Registers (or refreshes) a citizen wallet device. Idempotent on
    /// <c>(PlatformUserId, DevicePublicJwkThumbprint)</c>.
    /// </summary>
    Task<PlatformUserDeviceRegistrationResult> RegisterAsync(
        Guid platformUserId,
        string label,
        string devicePublicJwkThumbprint,
        string devicePublicJwkJson,
        string platform,
        string userAgent,
        DateTimeOffset delegationExpiresAt,
        string delegationCredentialJti,
        int statusListId,
        int statusListIndex,
        CancellationToken ct = default);

    /// <summary>
    /// Look up a single device by id, scoped to the supplied platform user.
    /// Returns null if the device does not exist or is not owned by the user.
    /// </summary>
    Task<PlatformUserDeviceLookupResult?> GetByIdAsync(
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a device as revoked on the Tenant Service. Idempotent. Used by
    /// the Wallet Service after a PWA-initiated <c>DELETE /api/v1/wallet/devices/{id}</c>
    /// has flipped the status-list bit on the wallet side, so the Tenant row
    /// reflects the revocation when the citizen views it from the main UI.
    /// Returns <c>false</c> when the device does not exist or is not owned by
    /// the supplied platform user (404 from Tenant — intentionally
    /// indistinguishable to avoid leaking device existence).
    /// </summary>
    Task<bool> RevokeAsync(
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists every device enrolled by the supplied platform user. Includes both
    /// active and revoked devices. Used by the Wallet Service to back the PWA's
    /// <c>GET /api/v1/wallet/devices</c> public endpoint.
    /// </summary>
    Task<IReadOnlyList<PlatformUserDeviceLookupResult>> ListAsync(
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Renames a device. Returns <c>false</c> when the device does not exist or
    /// is not owned by the supplied platform user (intentionally indistinguishable).
    /// Throws on invalid label (length 0 or &gt; 120).
    /// </summary>
    Task<bool> UpdateLabelAsync(
        Guid deviceId,
        Guid platformUserId,
        string label,
        CancellationToken ct = default);
}

/// <summary>Result of <see cref="IPlatformUserDeviceClient.RegisterAsync"/>.</summary>
/// <param name="DeviceId">Server-assigned device id (citizen-visible).</param>
/// <param name="EnrolledAt">Original enrolment timestamp (preserved across renewals).</param>
public sealed record PlatformUserDeviceRegistrationResult(
    Guid DeviceId,
    DateTimeOffset EnrolledAt);

/// <summary>Result of <see cref="IPlatformUserDeviceClient.GetByIdAsync"/>.</summary>
/// <param name="DeviceId">Identifier of the device.</param>
/// <param name="PlatformUserId">Identifier of the platform user.</param>
/// <param name="Label">The label.</param>
/// <param name="DevicePublicJwkThumbprint">The device public jwk thumbprint.</param>
/// <param name="DevicePublicJwkJson">The device public jwk json.</param>
/// <param name="Platform">The platform.</param>
/// <param name="Status">Current status of the resource.</param>
/// <param name="EnrolledAt">Timestamp at which enrolled occurred (UTC).</param>
/// <param name="DelegationExpiresAt">Timestamp at which delegation expires occurred (UTC).</param>
/// <param name="DelegationCredentialJti">The delegation credential jti.</param>
/// <param name="StatusListId">Identifier of the status list containing this device's revocation bit.</param>
/// <param name="StatusListIndex">Numeric value for status list index.</param>
public sealed record PlatformUserDeviceLookupResult(
    Guid DeviceId,
    Guid PlatformUserId,
    string Label,
    string DevicePublicJwkThumbprint,
    string DevicePublicJwkJson,
    string Platform,
    string Status,
    DateTimeOffset EnrolledAt,
    DateTimeOffset DelegationExpiresAt,
    string DelegationCredentialJti,
    int StatusListId,
    int StatusListIndex);
