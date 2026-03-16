// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

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

        group.MapPost("/", CreateOrganization)
            .WithName("AdminCreateOrganization")
            .WithSummary("Admin-initiated organisation creation with invite")
            .WithDescription("Creates a private org and invites the specified admin by email. " +
                "If the admin email matches an existing PlatformUser, they are added directly. " +
                "Otherwise, a pending invitation is created. Requires SystemAdmin role.")
            .RequireAuthorization("RequireSystemAdmin");

        // Additional endpoints will be added in US7 (T065-T067)

        return app;
    }

    /// <summary>
    /// Creates a new private organisation and invites an administrator.
    /// </summary>
    private static async Task<IResult> CreateOrganization(
        AdminCreateOrganizationRequest request,
        ClaimsPrincipal principal,
        IOrgProvisioningService provisioningService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Resolve the system admin's platform user ID from JWT claims
        var platformUserIdClaim = principal.FindFirst("platform_user_id")?.Value;
        if (string.IsNullOrEmpty(platformUserIdClaim) || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await provisioningService.AdminProvisionAsync(
            platformUserId,
            request.Name,
            request.Subdomain,
            request.Description,
            request.AdminEmail,
            request.Role,
            cancellationToken);

        if (!result.Success)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "error"] = [result.Error ?? "Validation failed."]
            });
        }

        var response = new AdminCreateOrganizationResponse
        {
            Success = true,
            OrganizationId = result.OrganizationId,
            OrganizationName = result.OrganizationName,
            Subdomain = result.Subdomain,
            AdminDirectlyAdded = result.AdminDirectlyAdded,
            InvitationId = result.InvitationId
        };

        return TypedResults.Created($"/api/platform/organizations/{result.OrganizationId}", response);
    }
}
