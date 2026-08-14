// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
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
            .WithRequestValidation()
            .WithName("AdminCreateOrganization")
            .WithSummary("Admin-initiated organisation creation with invite")
            .WithDescription("Creates a private org and invites the specified admin by email. " +
                "If the admin email matches an existing PlatformUser, they are added directly. " +
                "Otherwise, a pending invitation is created. Requires SystemAdmin role.")
            .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience");

        group.MapGet("/", ListOrganizations)
            .WithName("ListPlatformOrganizations")
            .WithSummary("List all organisations")
            .WithDescription("Returns paginated list of all orgs with metadata. System admin only.")
            .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience");

        group.MapPut("/{orgId:guid}/status", UpdateOrganizationStatus)
            .WithName("UpdateOrganizationStatus")
            .WithSummary("Update organisation status")
            .WithDescription("Changes org status (Active/Suspended). Platform orgs cannot be suspended.")
            .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience");

        group.MapGet("/{orgId:guid}/users", GetOrganizationUsers)
            .WithName("GetOrganizationUsersPlatform")
            .WithSummary("View organisation user list")
            .WithDescription("Returns paginated user list for an org. Read-only audit view.")
            .RequireAuthorization("RequirePlatformAuditor", "RequirePlatformAudience");

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
            cancellationToken,
            adminPassword: request.AdminPassword,
            adminDisplayName: request.AdminDisplayName,
            adminEmailVerified: request.AdminEmailVerified);

        if (!result.Success)
        {
            logger.LogWarning("Admin org creation failed for {AdminEmail}: {Error} ({ErrorCode})",
                request.AdminEmail, result.Error, result.ErrorCode);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "error"] = [result.Error ?? "Validation failed."]
            });
        }

        logger.LogInformation("Organisation {OrgId} ({OrgName}) created by platform user {PlatformUserId}",
            result.OrganizationId, result.OrganizationName, platformUserId);

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

    /// <summary>
    /// Returns a paginated list of all organisations with user counts.
    /// </summary>
    private static async Task<IResult> ListOrganizations(
        TenantDbContext db,
        CancellationToken ct,
        string? status,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Organizations.AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrganizationStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(o => o.Status == parsed);
        }

        var totalCount = await query.CountAsync(ct);

        var orgs = await query
            .OrderBy(o => o.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var orgIds = orgs.Select(o => o.Id).ToList();

        var userCounts = await db.PlatformUserOrgMemberships
            .Where(m => orgIds.Contains(m.OrganizationId))
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrgId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrgId, x => x.Count, ct);

        var items = orgs.Select(o => new OrganizationSummaryResponse
        {
            Id = o.Id,
            Name = o.Name,
            Subdomain = o.Subdomain,
            Status = o.Status,
            OrgType = o.OrgType,
            IsPlatformOrg = o.IsPlatformOrg,
            UserCount = userCounts.GetValueOrDefault(o.Id),
            CreatedAt = o.CreatedAt
        }).ToList();

        return TypedResults.Ok(new PlatformOrgListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Updates an organisation's status. Platform orgs cannot be suspended.
    /// </summary>
    private static async Task<IResult> UpdateOrganizationStatus(
        Guid orgId,
        UpdateOrgStatusRequest request,
        TenantDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request.Status is not (OrganizationStatus.Active or OrganizationStatus.Suspended))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Status"] = ["Status must be Active or Suspended."]
            });
        }

        var org = await db.Organizations.FindAsync([orgId], ct);
        if (org is null)
        {
            return TypedResults.NotFound();
        }

        if (org.IsPlatformOrg)
        {
            return TypedResults.Problem(
                detail: "Cannot change status of platform organisations.",
                statusCode: 400);
        }

        org.Status = request.Status;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Organisation {OrgId} status changed to {Status}", orgId, request.Status);

        var userCount = await db.PlatformUserOrgMemberships
            .CountAsync(m => m.OrganizationId == orgId, ct);

        return TypedResults.Ok(new OrganizationSummaryResponse
        {
            Id = org.Id,
            Name = org.Name,
            Subdomain = org.Subdomain,
            Status = org.Status,
            OrgType = org.OrgType,
            IsPlatformOrg = org.IsPlatformOrg,
            UserCount = userCount,
            CreatedAt = org.CreatedAt
        });
    }

    /// <summary>
    /// Returns a paginated list of users in an organisation (read-only audit view).
    /// </summary>
    private static async Task<IResult> GetOrganizationUsers(
        Guid orgId,
        TenantDbContext db,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20)
    {
        var orgExists = await db.Organizations.AnyAsync(o => o.Id == orgId, ct);
        if (!orgExists)
        {
            return TypedResults.NotFound();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.PlatformUserOrgMemberships
            .Where(m => m.OrganizationId == orgId)
            .Join(db.PlatformUsers, m => m.PlatformUserId, p => p.Id,
                (m, p) => new OrgUserSummaryResponse
                {
                    Id = p.Id,
                    Email = p.Email,
                    DisplayName = p.DisplayName,
                    Roles = new[] { m.Role },
                    Status = p.Status.ToString(),
                    CreatedAt = m.JoinedAt,
                    LastLoginAt = p.LastLoginAt
                });

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return TypedResults.Ok(new OrgUserListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }
}
