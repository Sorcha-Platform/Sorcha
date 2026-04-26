// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Server-side device-registration surface for citizen wallet PWA enrolment
/// (Feature 114). Owned by the Tenant Service because <see cref="PlatformUserDevice"/>
/// lives next to the platform identity (PlatformUser) which is the recovery
/// anchor for wallet keys.
/// </summary>
/// <remarks>
/// Called over service-to-service auth from the Wallet Service's enrolment
/// endpoint after the holder key has been derived and a device delegation
/// credential issued. Idempotent on <c>(PlatformUserId, DevicePublicJwkThumbprint)</c>
/// so a wallet retry after a transient failure does not create duplicate rows
/// or burn extra status-list slots.
/// </remarks>
public interface IPlatformUserDeviceService
{
    /// <summary>
    /// Registers (or returns the existing record for) a PlatformUser's enrolled device.
    /// </summary>
    /// <param name="platformUserId">The owning PlatformUser id.</param>
    /// <param name="label">Citizen-visible label, 1..120 chars.</param>
    /// <param name="devicePublicJwkThumbprint">RFC 7638 thumbprint (43 base64url chars).</param>
    /// <param name="devicePublicJwkJson">Canonical JWK JSON (≤512 chars).</param>
    /// <param name="platform">Free-form platform descriptor.</param>
    /// <param name="userAgent">Raw User-Agent header at enrolment.</param>
    /// <param name="delegationExpiresAt">Expiry of the device delegation credential.</param>
    /// <param name="delegationCredentialJti">JTI of the current delegation credential.</param>
    /// <param name="statusListIndex">Bit position allocated in the org's citizen-devices status list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The persisted device record. On a duplicate <c>(PlatformUserId, Thumbprint)</c>
    /// the existing record is returned with its delegation fields refreshed to the
    /// new values — the wallet's enrolment retry is a successful no-op.
    /// </returns>
    Task<PlatformUserDevice> RegisterAsync(
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
    /// Returns null if the device does not exist OR is not owned by the user
    /// (the two cases are intentionally indistinguishable to callers — both
    /// surface as "not your device" without leaking existence).
    /// </summary>
    Task<PlatformUserDevice?> GetByIdAsync(
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);
}
