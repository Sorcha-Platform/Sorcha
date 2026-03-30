// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Platform user management endpoints for system administrators.
/// Provides user provisioning, password reset, and account management.
/// </summary>
public static class PlatformManagementEndpoints
{
    /// <summary>
    /// Maps platform user management endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/users")
            .WithTags("Platform User Management");

        group.MapPost("/", ProvisionUser)
            .WithName("AdminProvisionUser")
            .WithSummary("Provision a platform user into an organisation")
            .WithDescription("Creates PlatformUser + UserIdentity + PlatformUserOrgMembership atomically. " +
                "If a PlatformUser with the same email already exists, it is reused and linked to the " +
                "target organisation. Supports optional password, role selection, and email verification skip. " +
                "Requires SystemAdmin role.")
            .RequireAuthorization("RequireSystemAdmin");

        return app;
    }

    /// <summary>
    /// Provisions a platform user into an organisation.
    /// Creates or reuses PlatformUser, creates UserIdentity and OrgMembership.
    /// </summary>
    private static async Task<IResult> ProvisionUser(
        AdminProvisionUserRequest request,
        IPlatformUserProvisioningService provisioningService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Inline validation
        var validationErrors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            validationErrors["Email"] = ["Email is required."];
        }
        else if (!new EmailAddressAttribute().IsValid(request.Email))
        {
            validationErrors["Email"] = ["Invalid email format."];
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            validationErrors["DisplayName"] = ["Display name is required."];
        }

        if (request.OrganizationId == Guid.Empty)
        {
            validationErrors["OrganizationId"] = ["Organization ID is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            validationErrors["Role"] = ["Role is required."];
        }

        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var result = await provisioningService.ProvisionUserAsync(request, cancellationToken);

        if (!result.Success)
        {
            return result.ErrorStatusCode switch
            {
                404 => TypedResults.NotFound(new { error = result.Error }),
                409 => TypedResults.Conflict(new { error = result.Error }),
                _ => result.ValidationErrors is not null
                    ? TypedResults.ValidationProblem(result.ValidationErrors)
                    : TypedResults.Problem(detail: result.Error, statusCode: result.ErrorStatusCode ?? 400)
            };
        }

        logger.LogInformation("Admin provisioned user {Email} into org {OrgId}",
            request.Email, request.OrganizationId);

        return TypedResults.Created($"/api/platform/users/{result.Response!.UserId}", result.Response);
    }
}
