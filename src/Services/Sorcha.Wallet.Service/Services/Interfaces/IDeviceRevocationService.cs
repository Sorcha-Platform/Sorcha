// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Composes the wallet-side citizen device revocation flow (Feature 114, US3):
/// flips the status-list bit via <see cref="ICitizenStatusListPublisher"/>
/// and broadcasts the <c>DeviceRevoked</c> SignalR event so any wallet PWA
/// still connected for the citizen receives it and locks itself.
/// </summary>
/// <remarks>
/// Shared between the public PWA-facing endpoint
/// (<c>DELETE /api/v1/wallet/devices/{id}</c>) and the internal
/// service-to-service endpoint
/// (<c>POST /api/internal/citizen-status-list/revoke</c>) called by Tenant
/// Service when the citizen revokes from the main UI. Pure Wallet-side —
/// callers are responsible for any Tenant-row update.
/// </remarks>
public interface IDeviceRevocationService
{
    /// <summary>
    /// Flips the status-list bit for the supplied <paramref name="organizationId"/>
    /// + <paramref name="listId"/> + <paramref name="indexInList"/> and broadcasts
    /// <c>DeviceRevoked(deviceId)</c> to the citizen's SignalR group. Idempotent —
    /// re-flipping an already-set bit is a successful no-op (the broadcast still
    /// fires so a late-connecting wallet locks itself).
    /// </summary>
    /// <param name="organizationId">Owning org (citizen's home org / delegation issuer).</param>
    /// <param name="listId">Status list id (per-org; rolls over at capacity).</param>
    /// <param name="indexInList">Bit position within the status list.</param>
    /// <param name="deviceId">Device being revoked — payload of the SignalR event.</param>
    /// <param name="platformUserId">Owning citizen — keys the SignalR group.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);
}
