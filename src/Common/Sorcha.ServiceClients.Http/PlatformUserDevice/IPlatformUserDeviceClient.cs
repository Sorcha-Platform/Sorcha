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
}

/// <summary>Result of <see cref="IPlatformUserDeviceClient.RegisterAsync"/>.</summary>
/// <param name="DeviceId">Server-assigned device id (citizen-visible).</param>
/// <param name="EnrolledAt">Original enrolment timestamp (preserved across renewals).</param>
public sealed record PlatformUserDeviceRegistrationResult(
    Guid DeviceId,
    DateTimeOffset EnrolledAt);

/// <summary>Result of <see cref="IPlatformUserDeviceClient.GetByIdAsync"/>.</summary>
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
    int StatusListIndex);
