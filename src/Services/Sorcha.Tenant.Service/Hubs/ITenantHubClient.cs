// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="TenantHub"/>.
/// </summary>
/// <remarks>
/// <para>
/// Created in Feature 118 Phase 4 (US2 — topology consolidation). The hub
/// itself is auth-only at this phase; inbox event methods land in Phase 5
/// (US3 — durable user inbox) along with membership / security / system
/// announcement events.
/// </para>
/// <para>
/// Method bodies are intentionally absent — defining the typed surface lets
/// services emit through <c>IHubContext&lt;TenantHub, ITenantHubClient&gt;</c>
/// once the corresponding write paths land. Each event method that is added
/// MUST conform to the thin-signal contract from Feature 118 spec FR-016 — FR-019:
/// opaque IDs only, paired with an authenticated REST detail endpoint
/// referenced via <c>&lt;see cref="..."/&gt;</c> in the XML doc.
/// </para>
/// </remarks>
public interface ITenantHubClient
{
    /// <summary>
    /// A new inbox entry was written for the user. Carries opaque IDs only;
    /// clients fetch full content via <c>GET /api/me/inbox/{entryId}</c>.
    /// </summary>
    /// <param name="entryId">GUID of the entry, formatted with <c>:N</c> (no hyphens).</param>
    /// <param name="occurredAt">Server timestamp at which the underlying event occurred.</param>
    /// <param name="traceId">W3C trace-id for correlating across the bridge.</param>
    Task InboxEntryAdded(string entryId, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// The authenticated user's unread inbox count changed. Authoritative —
    /// the client should overwrite, not increment. Cold-load via
    /// <c>GET /api/me/inbox/unread-count</c>.
    /// </summary>
    /// <param name="unreadCount">New unread count.</param>
    /// <param name="occurredAt">Server timestamp.</param>
    /// <param name="traceId">W3C trace-id for correlating across the bridge.</param>
    Task InboxUnreadCountUpdated(int unreadCount, DateTimeOffset occurredAt, string traceId);

    /// <summary>
    /// A new wallet device has been enrolled against the user's account.
    /// Thin-signal: opaque IDs only; clients fetch full device detail via
    /// <c>GET /api/v1/me/devices/{deviceId}</c>.
    /// </summary>
    /// <remarks>
    /// Published from <c>PlatformUserDeviceService.RegisterAsync</c> to the
    /// <see cref="TenantHubGroups.User"/> group of the bound platform user.
    /// Feature 126 — the council page subscribes to this event to advance
    /// out of the "Waiting for your phone…" state when the citizen finishes
    /// pairing on their wallet device. Idempotent: re-firing for the same
    /// <c>(platformUserId, deviceId)</c> pair is a no-op for the council page.
    /// </remarks>
    /// <param name="platformUserId">Platform user that owns the device.</param>
    /// <param name="deviceId">Id of the newly registered <c>PlatformUserDevice</c>.</param>
    Task DeviceEnrolled(Guid platformUserId, Guid deviceId);

    // Membership / Security / System announcement events follow in a later phase.
}
