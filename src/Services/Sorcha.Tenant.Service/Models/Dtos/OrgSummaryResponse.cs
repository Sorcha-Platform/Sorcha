// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Compact, dashboard-card-shaped summary of an organization's state.
/// Feature 131 / UX-005 — backs the org-scoped view of the API Gateway's
/// <c>/api/dashboard</c> endpoint.
/// </summary>
/// <remarks>
/// Distinct from <see cref="DashboardResponse"/>, which is the admin dashboard's
/// richer per-org view (role breakdowns, recent logins, IDP status). This DTO is
/// scoped to the four numbers visible on Home.razor's stats grid.
/// </remarks>
public record OrgSummaryResponse
{
    /// <summary>Organization id.</summary>
    public Guid OrgId { get; init; }

    /// <summary>Count of active <c>UserIdentity</c> rows in the org.</summary>
    public int ActiveUsers { get; init; }

    /// <summary>Count of pending, unexpired invitations in the org.</summary>
    public int PendingInvitations { get; init; }

    /// <summary>Count of <c>OrganizationRegisterSubscriptions</c> rows with Status=Active.</summary>
    public int SubscribedRegisters { get; init; }

    /// <summary>
    /// Sum of transaction counts across the org's subscribed registers (Status=Active).
    /// Best-effort: if the Register Service is unreachable, this is 0 and a warning is logged.
    /// </summary>
    public int RecentTransactions { get; init; }

    /// <summary>Snapshot timestamp (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
