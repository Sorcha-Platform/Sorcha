// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;

using Sorcha.Tenant.Service.Authorization;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Admin dashboard API endpoints.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Maps dashboard endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/dashboard")
            .WithTags("Dashboard")
            // Spec 136: platform-tier admin surface — tier gate (RequirePlatformAudience) composes
            // ON TOP OF the role gate, so a consumer token is refused even if it carried an admin role.
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            // B2+ (2026-07-29 review): neither gate compared the caller to {organizationId}, so an
            // administrator of one organisation could read another's dashboard.
            .RequireCallerOrganization();

        group.MapGet("/", GetDashboard)
            .WithName("GetDashboard")
            .WithSummary("Get admin dashboard statistics")
            .WithDescription("Returns aggregated statistics including active/suspended user counts, role distribution, recent logins, pending invitations, and IDP configuration status.")
            .Produces<DashboardResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Feature 131 / UX-005 — compact org-summary backing the API Gateway's
        // /api/dashboard endpoint in org-scope. Broader-access than the
        // Administrator-only /dashboard above (any signed-in org member can read
        // the four card numbers).
        app.MapGet("/api/organizations/{organizationId:guid}/dashboard-summary", GetOrgSummary)
            .WithTags("Dashboard")
            .RequireAuthorization()
            // Broader ROLE access than /dashboard (any signed-in org member), but still strictly
            // org-scoped: "any member" must mean any member OF THIS ORGANISATION.
            .RequireCallerOrganization()
            .WithName("GetOrgDashboardSummary")
            .WithSummary("Get the org-scoped dashboard summary")
            .WithDescription("Returns active users, pending invitations, subscribed registers, and recent transactions across subscribed registers — the four cards rendered on Home.razor in org-scope view.")
            .Produces<OrgSummaryResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Ok<DashboardResponse>> GetDashboard(
        Guid organizationId,
        IDashboardService dashboardService,
        CancellationToken cancellationToken)
    {
        var dashboard = await dashboardService.GetDashboardAsync(organizationId, cancellationToken);
        return TypedResults.Ok(dashboard);
    }

    private static async Task<Ok<OrgSummaryResponse>> GetOrgSummary(
        Guid organizationId,
        IDashboardService dashboardService,
        CancellationToken cancellationToken)
    {
        var summary = await dashboardService.GetOrgSummaryAsync(organizationId, cancellationToken);
        return TypedResults.Ok(summary);
    }
}
