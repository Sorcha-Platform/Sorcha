// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Platform organisation management endpoints.
/// Provides system admin capabilities: list orgs, create orgs, manage org status, audit user lists.
/// </summary>
public static class PlatformOrgEndpoints
{
    /// <summary>
    /// Maps platform organisation management endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformOrgEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/organizations")
            .WithTags("Platform Organisations");

        // Endpoints will be added in US5 (T055) and US7 (T065-T067)

        return app;
    }
}
