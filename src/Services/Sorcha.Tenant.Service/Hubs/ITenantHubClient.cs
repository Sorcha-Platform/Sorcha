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
    // No event methods yet — added in Phase 5 (InboxEntryAdded, InboxUnreadCountUpdated)
    // and later in Phase 4 follow-up work (MembershipChanged, SecurityAlert, SystemAnnouncement).
    // Keeping the interface empty rather than carrying placeholders so Phase 5 owners
    // see immediately that they are defining the first real event method.
}
