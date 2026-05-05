// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// Group-name builder for <see cref="BlueprintHub"/>. All BlueprintHub group strings
/// MUST be constructed via these helpers — no inline interpolation in service code.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-013 — FR-015. The CI grep gate
/// (<c>scripts/check-no-inline-group-strings.ps1</c>, Phase 7 / US5) enforces this
/// retroactively across every notification hub.
/// </remarks>
public static class BlueprintHubGroups
{
    /// <summary>Per-wallet group. Hosts action-availability and workflow events targeting the wallet.</summary>
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";

    /// <summary>Per-instance group. Hosts instance-lifecycle events (state transitions).</summary>
    public static string Instance(Guid instanceId) => $"instance:{instanceId:N}";

    /// <summary>Per-organisation group. Hosts org-scoped workflow events.</summary>
    public static string Org(Guid orgId) => $"org:{orgId:N}";

    // ---- Legacy EventsHub-shaped groups (parallel-fire window) ----

    /// <summary>
    /// Per-user group used by the legacy EventsHub during the parallel-fire window.
    /// Identical shape to <c>TenantHubGroups.User</c> but lives here so EventsHub —
    /// which physically resides in Blueprint Service — does not need a cross-service
    /// project reference. Removed when EventsHub retires (spec FR-038).
    /// </summary>
    /// <remarks>
    /// Accepts a string id without parsing because Blueprint's EventsHub reads the
    /// claim as raw text and groups by it directly; the typed-Guid form is the
    /// preferred shape for new code on TenantHub.
    /// </remarks>
    public static string LegacyEventsHubUser(string userId) => $"user:{userId}";

    /// <summary>
    /// Per-organisation group used by the legacy EventsHub during the parallel-fire
    /// window. See <see cref="LegacyEventsHubUser"/> for rationale.
    /// </summary>
    public static string LegacyEventsHubOrg(string orgId) => $"org:{orgId}";
}
