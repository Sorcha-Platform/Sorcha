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

    /// <summary>
    /// Gets the compact org-summary used by the API Gateway's <c>/api/dashboard</c> endpoint
    /// in org-scope (Feature 131 / UX-005). Includes active-user count, pending-invitation count,
    /// subscribed-register count, and recent-transaction sum across the org's subscribed registers.
    /// </summary>
    Task<OrgSummaryResponse> GetOrgSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
