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
    /// <param name="statusListId">Identifier of the status list (per-org; rolls over at capacity).</param>
    /// <param name="statusListIndex">Bit position allocated within the status list.</param>
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
        int statusListId,
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

    /// <summary>
    /// List every device enrolled by the supplied platform user, ordered by
    /// <see cref="PlatformUserDevice.EnrolledAt"/> descending. Includes both
    /// active and revoked devices — the citizen needs to see revoked devices
    /// in their device list to confirm a revocation took effect.
    /// </summary>
    Task<IReadOnlyList<PlatformUserDevice>> ListAsync(
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Aggregate query for the F128 cold-start surfaces: does this user have
    /// at least one ACTIVE (non-revoked) paired device? If yes, returns the
    /// most recent enrolment timestamp; if no, returns
    /// <c>(false, null)</c>.
    /// </summary>
    /// <remarks>
    /// Used by the wallet PWA pairing takeover trigger (FR-010) and the
    /// Sorcha Web nag-banner trigger (FR-024) via the shared
    /// <c>IHasPairedDeviceProbe</c> client service. Does not expose the
    /// per-device list — the nag-banner doesn't need that detail and a
    /// boolean aggregate is cheaper to cache + invalidate.
    /// </remarks>
    Task<(bool HasAnyDevice, DateTimeOffset? LatestEnrolledAt)> HasAnyAsync(
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes the device by setting <see cref="PlatformUserDevice.Status"/> to
    /// <see cref="PlatformUserDeviceStatus.Revoked"/>, recording the revocation
    /// timestamp and the platform user who triggered it. Idempotent: a
    /// re-revocation against an already-revoked device is a successful no-op
    /// (the original <c>RevokedAt</c> / <c>RevokedByPlatformUserId</c> are
    /// preserved).
    /// </summary>
    /// <returns>
    /// The device after the revocation, or <c>null</c> if the device does not
    /// exist OR is not owned by <paramref name="platformUserId"/> (intentionally
    /// indistinguishable — see <see cref="GetByIdAsync"/>).
    /// </returns>
    /// <remarks>
    /// This implementation only flips the Tenant-side status. Propagation to the
    /// Wallet Service status-list bit is performed by callers (the endpoint
    /// layer) in PR2; keeping the service local-only here avoids tangling the
    /// service-to-service call into the unit-of-work boundary.
    /// </remarks>
    Task<PlatformUserDevice?> RevokeAsync(
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the citizen-visible <see cref="PlatformUserDevice.Label"/>. Returns
    /// <c>null</c> when the device does not exist OR is not owned by the supplied
    /// platform user (intentionally indistinguishable). Validates length 1..120.
    /// </summary>
    Task<PlatformUserDevice?> UpdateLabelAsync(
        Guid deviceId,
        Guid platformUserId,
        string label,
        CancellationToken ct = default);
}
