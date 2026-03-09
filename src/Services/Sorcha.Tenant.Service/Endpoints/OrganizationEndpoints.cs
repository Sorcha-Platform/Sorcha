// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Organization management API endpoints.
/// </summary>
public static class OrganizationEndpoints
{
    /// <summary>
    /// Maps organization management endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        // Organization CRUD
        group.MapPost("/", CreateOrganization)
            .WithName("CreateOrganization")
            .WithSummary("Create a new organization")
            .WithDescription("Creates a new organization. The authenticated user becomes the organization administrator.")
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/", ListOrganizations)
            .WithName("ListOrganizations")
            .WithSummary("List organizations")
            .WithDescription("Lists all organizations. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<OrganizationListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetOrganization)
            .WithName("GetOrganization")
            .WithSummary("Get organization details")
            .WithDescription("Gets details of a specific organization.")
            .Produces<OrganizationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/by-subdomain/{subdomain}", GetOrganizationBySubdomain)
            .WithName("GetOrganizationBySubdomain")
            .WithSummary("Get organization by subdomain")
            .WithDescription("Gets an organization by its subdomain.")
            .AllowAnonymous()
            .Produces<OrganizationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/stats", GetOrganizationStats)
            .WithName("GetOrganizationStats")
            .WithSummary("Get organization statistics")
            .WithDescription("Gets count of active organizations. Public endpoint for dashboard.")
            .AllowAnonymous()
            .Produces<OrganizationStatsResponse>();

        group.MapPut("/{id:guid}", UpdateOrganization)
            .WithName("UpdateOrganization")
            .WithSummary("Update an organization")
            .WithDescription("Updates an existing organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<OrganizationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeactivateOrganization)
            .WithName("DeactivateOrganization")
            .WithSummary("Deactivate an organization")
            .WithDescription("Soft deletes an organization. Data retained for 30 days. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/validate-subdomain/{subdomain}", ValidateSubdomain)
            .WithName("ValidateSubdomain")
            .WithSummary("Validate subdomain availability")
            .WithDescription("Checks if a subdomain is valid and available.")
            .AllowAnonymous()
            .Produces<SubdomainValidationResponse>(StatusCodes.Status200OK)
            .Produces<SubdomainValidationResponse>(StatusCodes.Status400BadRequest);

        // User management within organization
        group.MapPost("/{organizationId:guid}/users", AddUserToOrganization)
            .WithName("AddUserToOrganization")
            .WithSummary("Add user to organization")
            .WithDescription("Adds a user to the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{organizationId:guid}/users", GetOrganizationUsers)
            .WithName("GetOrganizationUsers")
            .WithSummary("List organization users")
            .WithDescription("Lists all users in the organization.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<UserListResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{organizationId:guid}/users/{userId:guid}", GetOrganizationUser)
            .WithName("GetOrganizationUser")
            .WithSummary("Get organization user")
            .WithDescription("Gets details of a specific user in the organization.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<UserResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{organizationId:guid}/users/{userId:guid}", UpdateOrganizationUser)
            .WithName("UpdateOrganizationUser")
            .WithSummary("Update organization user")
            .WithDescription("Updates a user in the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{organizationId:guid}/users/{userId:guid}", RemoveUserFromOrganization)
            .WithName("RemoveUserFromOrganization")
            .WithSummary("Remove user from organization")
            .WithDescription("Removes a user from the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // User lifecycle management
        group.MapPost("/{organizationId:guid}/users/{userId:guid}/unlock", UnlockUser)
            .WithName("UnlockUser")
            .WithSummary("Unlock a locked user account")
            .WithDescription("Resets the failed login counter and removes lockout for a user account.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/{userId:guid}/suspend", SuspendUser)
            .WithName("SuspendUser")
            .WithSummary("Suspend a user account")
            .WithDescription("Suspends a user account, preventing authentication. Active sessions are invalidated.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/{userId:guid}/reactivate", ReactivateUser)
            .WithName("ReactivateUser")
            .WithSummary("Reactivate a suspended user account")
            .WithDescription("Reactivates a previously suspended user account.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{organizationId:guid}/users/{userId:guid}/role", ChangeUserRole)
            .WithName("ChangeUserRole")
            .WithSummary("Change a user's role")
            .WithDescription("Changes a user's role. Cannot target SystemAdmin users or assign SystemAdmin role.")
            .RequireAuthorization("RequireAdministrator")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok, NotFound>> UnlockUser(
        Guid organizationId,
        Guid userId,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var targetUser = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (targetUser == null || targetUser.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        targetUser.FailedLoginCount = 0;
        targetUser.LockedUntil = null;
        targetUser.LockedPermanently = false;
        await identityRepository.UpdateUserAsync(targetUser, cancellationToken);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = GetUserId(user),
            EventType = AuditEventType.AccountUnlockedByAdmin,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["targetUserId"] = userId.ToString(),
                ["targetEmail"] = targetUser.Email
            }
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, NotFound>> SuspendUser(
        Guid organizationId,
        Guid userId,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var targetUser = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (targetUser == null || targetUser.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        targetUser.Status = IdentityStatus.Suspended;
        await identityRepository.UpdateUserAsync(targetUser, cancellationToken);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = GetUserId(user),
            EventType = AuditEventType.UserUpdatedInOrganization,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["targetUserId"] = userId.ToString(),
                ["action"] = "Suspended"
            }
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, BadRequest<ProblemDetails>, NotFound>> ReactivateUser(
        Guid organizationId,
        Guid userId,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var targetUser = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (targetUser == null || targetUser.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        if (targetUser.Status != IdentityStatus.Suspended)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid Operation",
                Detail = $"User is {targetUser.Status}, not Suspended. Only suspended users can be reactivated.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        targetUser.Status = IdentityStatus.Active;
        await identityRepository.UpdateUserAsync(targetUser, cancellationToken);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = GetUserId(user),
            EventType = AuditEventType.UserUpdatedInOrganization,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["targetUserId"] = userId.ToString(),
                ["action"] = "Reactivated"
            }
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, ValidationProblem, NotFound>> ChangeUserRole(
        Guid organizationId,
        Guid userId,
        ChangeUserRoleRequest request,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // Validate requested role
        if (request.Role == UserRole.SystemAdmin)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = ["Cannot assign SystemAdmin role."]
            });
        }

        var targetUser = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (targetUser == null || targetUser.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        // Cannot change role of SystemAdmin users
        if (targetUser.Roles.Contains(UserRole.SystemAdmin))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["Cannot change role of a SystemAdmin user."]
            });
        }

        var previousRole = targetUser.Roles.FirstOrDefault().ToString();
        targetUser.Roles = [request.Role];
        await identityRepository.UpdateUserAsync(targetUser, cancellationToken);

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = GetUserId(user),
            EventType = AuditEventType.UserUpdatedInOrganization,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["targetUserId"] = userId.ToString(),
                ["action"] = "RoleChanged",
                ["previousRole"] = previousRole,
                ["newRole"] = request.Role.ToString()
            }
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }

    private static async Task<Results<Created<OrganizationResponse>, Conflict<ProblemDetails>, ValidationProblem>> CreateOrganization(
        CreateOrganizationRequest request,
        IOrganizationService organizationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["user"] = ["User ID not found in claims"]
            });
        }

        try
        {
            var response = await organizationService.CreateOrganizationAsync(request, userId, cancellationToken);
            return TypedResults.Created($"/api/organizations/{response.Id}", response);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("already taken"))
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Subdomain Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
    }

    private static async Task<Ok<OrganizationListResponse>> ListOrganizations(
        IOrganizationService organizationService,
        bool includeInactive = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await organizationService.ListOrganizationsAsync(includeInactive, cancellationToken);

        // Apply pagination
        var skip = (pageNumber - 1) * pageSize;
        var paginatedOrgs = response.Organizations
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        var paginatedResponse = new OrganizationListResponse
        {
            Organizations = paginatedOrgs,
            TotalCount = response.TotalCount
        };

        return TypedResults.Ok(paginatedResponse);
    }

    private static async Task<Results<Ok<OrganizationResponse>, NotFound>> GetOrganization(
        Guid id,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var response = await organizationService.GetOrganizationAsync(id, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<OrganizationResponse>, NotFound>> GetOrganizationBySubdomain(
        string subdomain,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var response = await organizationService.GetOrganizationBySubdomainAsync(subdomain, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<OrganizationResponse>, NotFound, ValidationProblem>> UpdateOrganization(
        Guid id,
        UpdateOrganizationRequest request,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await organizationService.UpdateOrganizationAsync(id, request, cancellationToken);
            return response != null
                ? TypedResults.Ok(response)
                : TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound>> DeactivateOrganization(
        Guid id,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var success = await organizationService.DeactivateOrganizationAsync(id, cancellationToken);
        return success
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<SubdomainValidationResponse>, BadRequest<SubdomainValidationResponse>>> ValidateSubdomain(
        string subdomain,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var (isValid, errorMessage) = await organizationService.ValidateSubdomainAsync(subdomain, cancellationToken);

        var response = new SubdomainValidationResponse
        {
            Subdomain = subdomain,
            IsValid = isValid,
            ErrorMessage = errorMessage
        };

        return isValid
            ? TypedResults.Ok(response)
            : TypedResults.BadRequest(response);
    }

    private static async Task<Results<Created<UserResponse>, NotFound, ValidationProblem>> AddUserToOrganization(
        Guid organizationId,
        AddUserToOrganizationRequest request,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await organizationService.AddUserToOrganizationAsync(
                organizationId, request, cancellationToken);
            return TypedResults.Created(
                $"/api/organizations/{organizationId}/users/{response.Id}", response);
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not found"))
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = [ex.Message]
            });
        }
    }

    private static async Task<Ok<UserListResponse>> GetOrganizationUsers(
        Guid organizationId,
        IOrganizationService organizationService,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var response = await organizationService.GetOrganizationUsersAsync(
            organizationId, includeInactive, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<UserResponse>, NotFound>> GetOrganizationUser(
        Guid organizationId,
        Guid userId,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var response = await organizationService.GetOrganizationUserAsync(
            organizationId, userId, cancellationToken);
        return response != null
            ? TypedResults.Ok(response)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<UserResponse>, NotFound, ValidationProblem>> UpdateOrganizationUser(
        Guid organizationId,
        Guid userId,
        UpdateUserRequest request,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await organizationService.UpdateOrganizationUserAsync(
                organizationId, userId, request, cancellationToken);
            return response != null
                ? TypedResults.Ok(response)
                : TypedResults.NotFound();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound>> RemoveUserFromOrganization(
        Guid organizationId,
        Guid userId,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var success = await organizationService.RemoveUserFromOrganizationAsync(
            organizationId, userId, cancellationToken);
        return success
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private static async Task<Ok<OrganizationStatsResponse>> GetOrganizationStats(
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var response = await organizationService.GetOrganizationStatsAsync(cancellationToken);
        return TypedResults.Ok(response);
    }
}

/// <summary>
/// Organization statistics response.
/// </summary>
public record OrganizationStatsResponse
{
    /// <summary>
    /// Total number of active organizations.
    /// </summary>
    public int TotalOrganizations { get; init; }

    /// <summary>
    /// Total number of users across all organizations.
    /// </summary>
    public int TotalUsers { get; init; }
}

/// <summary>
/// Subdomain validation response.
/// </summary>
public record SubdomainValidationResponse
{
    /// <summary>
    /// The subdomain that was validated.
    /// </summary>
    public string Subdomain { get; init; } = string.Empty;

    /// <summary>
    /// Whether the subdomain is valid and available.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
