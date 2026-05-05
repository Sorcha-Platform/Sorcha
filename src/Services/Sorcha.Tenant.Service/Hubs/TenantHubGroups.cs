// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Hubs;

/// <summary>
/// Group-name builder for <see cref="TenantHub"/>. All TenantHub group strings
/// MUST be constructed via these helpers — no inline interpolation in service code.
/// </summary>
/// <remarks>
/// Per Feature 118 spec FR-013 — FR-015. The CI grep gate
/// (<c>scripts/check-no-inline-group-strings.ps1</c>, Phase 7 / US5) enforces this
/// retroactively across every notification hub.
/// </remarks>
public static class TenantHubGroups
{
    /// <summary>Per-user group. Includes inbox entries, membership changes, security alerts.</summary>
    public static string User(Guid platformUserId) => $"user:{platformUserId:N}";

    /// <summary>Per-organisation group. Includes admin events, member changes.</summary>
    public static string Org(Guid orgId) => $"org:{orgId:N}";

    /// <summary>Platform-wide system announcement group. Admin-only emit, gated server-side.</summary>
    public const string SystemAll = "system:all";
}
