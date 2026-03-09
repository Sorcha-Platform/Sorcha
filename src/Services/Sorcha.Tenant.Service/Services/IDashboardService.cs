// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for aggregating admin dashboard statistics.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets dashboard statistics for an organization.
    /// </summary>
    Task<DashboardResponse> GetDashboardAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
