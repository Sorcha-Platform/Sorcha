// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Dashboard;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for fetching aggregated dashboard statistics.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets aggregated dashboard statistics from the API Gateway.
    /// </summary>
    Task<DashboardStatsViewModel> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
}
