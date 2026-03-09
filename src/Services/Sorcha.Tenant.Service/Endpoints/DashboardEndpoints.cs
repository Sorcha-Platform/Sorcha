// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;

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
            .RequireAuthorization("RequireAdministrator");

        group.MapGet("/", GetDashboard)
            .WithName("GetDashboard")
            .WithSummary("Get admin dashboard statistics")
            .WithDescription("Returns aggregated statistics including active/suspended user counts, role distribution, recent logins, pending invitations, and IDP configuration status.")
            .Produces<DashboardResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

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
}
