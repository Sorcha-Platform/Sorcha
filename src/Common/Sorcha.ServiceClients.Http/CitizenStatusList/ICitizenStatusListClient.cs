// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.CitizenStatusList;

/// <summary>
/// Service-to-service client for citizen device revocation status-list flips
/// on the Wallet Service (Feature 114). Used by Tenant Service after a citizen
/// revokes a device from the main UI's <c>DELETE /api/v1/me/devices/{id}</c>
/// flow — Tenant flips its local row then dispatches to Wallet to flip the
/// status-list bit and broadcast the SignalR <c>DeviceRevoked</c> event so
/// any wallet PWA still connected for that user locks itself.
/// </summary>
public interface ICitizenStatusListClient
{
    /// <summary>
    /// Flips the citizen-devices status-list bit and broadcasts the
    /// <c>DeviceRevoked</c> SignalR event. Idempotent — a re-flip of an
    /// already-set bit is a successful no-op on the Wallet side.
    /// </summary>
    /// <param name="organizationId">Owning tenant org (citizen's home org / delegation issuer).</param>
    /// <param name="listId">Status list id (per-org; rolls over at capacity).</param>
    /// <param name="indexInList">Bit position within the status list.</param>
    /// <param name="deviceId">Device being revoked — used as the SignalR event payload so the wallet PWA can match against its own enrolment id.</param>
    /// <param name="platformUserId">Owning citizen — the SignalR group is keyed off this id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default);
}
