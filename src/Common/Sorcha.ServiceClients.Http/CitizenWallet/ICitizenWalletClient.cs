// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.ServiceClients.CitizenWallet;

/// <summary>
/// HTTP client used by the citizen wallet PWA (and any other consumer such as
/// E2E tests or the reference verifier setup harness) to call the citizen-wallet
/// endpoint surface on Sorcha.Wallet.Service (Feature 114).
/// </summary>
/// <remarks>
/// Methods are added to this interface as the matching server endpoints land —
/// see <c>specs/114-citizen-wallet-pwa/contracts/openapi-wallet-service.yaml</c>
/// for the full v1 surface. This is the wallet PWA's only ingress point into
/// the Sorcha service mesh.
/// </remarks>
public interface ICitizenWalletClient
{
    /// <summary>
    /// Enrols a device for the authenticated citizen. Calls
    /// <c>POST /api/v1/wallet/devices/enrol</c>; returns the issued device
    /// delegation credential plus the citizen's holder JWK and status-list
    /// pointer.
    /// </summary>
    Task<DeviceEnrolmentResponse> EnrolDeviceAsync(
        DeviceEnrolmentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Pulls credential and delegation deltas since the last sync. Calls
    /// <c>GET /api/v1/wallet/sync?since=...</c>. On a stale cursor the server
    /// returns 410; this method returns <c>null</c> to signal the caller should
    /// fall back to <see cref="ListCredentialsAsync"/>.
    /// </summary>
    Task<SyncResponse?> SyncAsync(string? sinceToken, CancellationToken ct = default);

    /// <summary>
    /// Returns the full credential snapshot for the authenticated citizen.
    /// Calls <c>GET /api/v1/wallet/credentials</c>. Used to seed a fresh wallet
    /// or to recover after a stale-cursor 410.
    /// </summary>
    Task<CredentialListResponse> ListCredentialsAsync(CancellationToken ct = default);

    /// <summary>
    /// Renews the device delegation credential for the supplied device id.
    /// Calls <c>POST /api/v1/wallet/devices/renew-delegation</c>. Returns
    /// <c>null</c> on 404 (device unknown or not owned by caller).
    /// </summary>
    Task<DelegationRenewalResponse?> RenewDelegationAsync(
        Guid deviceId, CancellationToken ct = default);

    /// <summary>
    /// Lists the citizen's enrolled devices (active + revoked, ordered by
    /// enrolment desc). Calls <c>GET /api/v1/wallet/devices</c>.
    /// </summary>
    Task<DeviceListResponse> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Renames a device. Calls <c>PUT /api/v1/wallet/devices/{id}/label</c>.
    /// Returns <c>false</c> on 404 (unknown / not owned).
    /// </summary>
    Task<bool> RenameDeviceAsync(
        Guid deviceId, string label, CancellationToken ct = default);

    /// <summary>
    /// Revokes a device. Calls <c>DELETE /api/v1/wallet/devices/{id}</c>.
    /// Returns <c>false</c> on 404 (unknown / not owned).
    /// </summary>
    Task<bool> RevokeDeviceAsync(
        Guid deviceId, CancellationToken ct = default);

    /// <summary>
    /// Reports a batch of locally-recorded presentations to the platform (US5).
    /// Calls <c>POST /api/v1/wallet/presentations/log</c>. Returns <c>true</c> when
    /// the server accepted the batch (202); duplicates are absorbed server-side by
    /// per-entry id dedupe, so re-reporting is safe.
    /// </summary>
    Task<bool> ReportPresentationLogAsync(
        PresentationLogReportRequest request, CancellationToken ct = default);
}
