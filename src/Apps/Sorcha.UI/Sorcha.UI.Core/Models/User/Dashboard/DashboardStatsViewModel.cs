// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Dashboard;

/// <summary>
/// View model for dashboard statistics cards.
/// Property names map to the API Gateway's /api/dashboard response (camelCase JSON).
/// </summary>
public record DashboardStatsViewModel
{
    [JsonPropertyName("totalBlueprints")]
    public int ActiveBlueprints { get; init; }

    [JsonPropertyName("totalWallets")]
    public int TotalWallets { get; init; }

    [JsonPropertyName("totalTransactions")]
    public int RecentTransactions { get; init; }

    [JsonPropertyName("connectedPeers")]
    public int ConnectedPeers { get; init; }

    [JsonPropertyName("totalRegisters")]
    public int ActiveRegisters { get; init; }

    [JsonPropertyName("totalTenants")]
    public int TotalOrganizations { get; init; }

    [JsonIgnore]
    public bool IsLoaded { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}
