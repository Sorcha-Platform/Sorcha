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
}
