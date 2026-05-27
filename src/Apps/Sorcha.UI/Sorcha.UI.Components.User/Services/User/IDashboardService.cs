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
    /// Gets aggregated dashboard statistics from the API Gateway. The response is
    /// org-scoped by default; pass <paramref name="scope"/> = "platform" to request
    /// the platform-wide view (honoured only when the caller has the SystemAdmin role).
    /// </summary>
    Task<DashboardStatsViewModel> GetDashboardStatsAsync(string? scope = null, CancellationToken cancellationToken = default);
}
