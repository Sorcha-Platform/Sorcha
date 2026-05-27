// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Dashboard;

/// <summary>
/// View model for dashboard statistics cards.
/// Property names map to the API Gateway's /api/dashboard response (camelCase JSON).
/// Feature 131 / UX-005: the response is one of two shapes discriminated by
/// <see cref="Scope"/> — "org" (four org-scoped cards) or "platform" (six
/// platform-wide cards). Fields not in the current shape arrive as null.
/// </summary>
public record DashboardStatsViewModel
{
    /// <summary>"org" | "platform" — discriminates the response shape.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "platform";

    /// <summary>Org id when <see cref="Scope"/> is "org"; null in platform shape.</summary>
    [JsonPropertyName("orgId")]
    public Guid? OrgId { get; init; }

    // --- Platform-shape fields (SystemAdmin platform view) ------------------

    [JsonPropertyName("totalBlueprints")]
    public int? ActiveBlueprints { get; init; }

    [JsonPropertyName("totalWallets")]
    public int? TotalWallets { get; init; }

    [JsonPropertyName("totalTransactions")]
    public int? PlatformRecentTransactions { get; init; }

    [JsonPropertyName("connectedPeers")]
    public int? ConnectedPeers { get; init; }

    [JsonPropertyName("totalRegisters")]
    public int? PlatformActiveRegisters { get; init; }

    [JsonPropertyName("totalTenants")]
    public int? TotalOrganizations { get; init; }

    // --- Org-shape fields (default view for every role) ---------------------

    [JsonPropertyName("activeUsers")]
    public int? ActiveUsers { get; init; }

    [JsonPropertyName("pendingInvitations")]
    public int? PendingInvitations { get; init; }

    [JsonPropertyName("subscribedRegisters")]
    public int? SubscribedRegisters { get; init; }

    [JsonPropertyName("recentTransactions")]
    public int? OrgRecentTransactions { get; init; }

    // --- Meta ---------------------------------------------------------------

    [JsonIgnore]
    public bool IsLoaded { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}
