// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Security.Claims;
using Sorcha.ServiceClients.Auth;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

using Sorcha.Tenant.Service.Authorization;

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

        // #1525 — the ORG ADMIN records the wallet they created for their own organisation.
        //
        // Deliberately NOT .RequireCallerOrganization(): that gate exempts platform SystemAdmins, and
        // this is the one endpoint where that override must not apply. The wallet's recovery phrase
        // belongs to the organisation, so a platform admin provisioning it on their behalf is the
        // exact failure being designed out — the phrase would be shown to the wrong person, or, as
        // before, to nobody at all. The handler compares the caller's org to the route itself.
        group.MapPost("/{organizationId:guid}/wallet", LinkOrganizationWallet)
            .WithName("LinkOrganizationWallet")
            .WithSummary("Record the wallet this organisation's admin created as its signing wallet")
            .WithDescription(
                "Second half of create-then-link. The org admin first creates a wallet against the "
                + "Wallet Service (POST /api/v1/wallets) with the organisation as owner, which returns "
                + "the BIP39 recovery phrase ONCE — it is never stored and cannot be recovered, and it "
                + "is the organisation's secret, not the platform's. This endpoint then records that "
                + "wallet as the organisation's canonical signing wallet, after verifying the "
                + "organisation owns it. Callable only by an administrator OF THAT organisation: a "
                + "platform admin cannot do it on their behalf, because they would be holding a secret "
                + "that is not theirs. Returns 409 if the organisation already has a wallet — "
                + "replacing it would orphan every credential issued under the old one.")
            .RequireAuthorization(AuthorizationPolicies.RequireAdministrator)
            .Produces<OrganizationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // Organization CRUD
        group.MapPost("/", CreateOrganization)
            .WithRequestValidation()
            .WithName("CreateOrganization")
            .WithSummary("Create a new organization")
            .WithDescription("Creates a new organization. The authenticated user becomes the organization administrator.")
            .Produces<OrganizationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "name": "Acme Corporation",
                      "subdomain": "acme-corp",
                      "branding": {
                        "primaryColor": "#2563EB",
                        "companyTagline": "Building the future"
                      }
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "201", """
                    {
                      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "name": "Acme Corporation",
                      "subdomain": "acme-corp",
                      "status": "Active",
                      "createdAt": "2026-03-15T10:30:00Z",
                      "branding": {
                        "primaryColor": "#2563EB",
                        "companyTagline": "Building the future"
                      }
                    }
                    """);
                return operation;
            });

        group.MapGet("/", ListOrganizations)
            .WithName("ListOrganizations")
            .WithSummary("List organizations")
            .WithDescription("Lists all organizations. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
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
            .WithRequestValidation()
            .WithName("UpdateOrganization")
            .WithSummary("Update an organization")
            .WithDescription("Updates an existing organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<OrganizationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", DeactivateOrganization)
            .WithName("DeactivateOrganization")
            .WithSummary("Deactivate an organization")
            .WithDescription("Soft deletes an organization. Data retained for 30 days. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
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
            .RequireCallerOrganization()
            .WithName("AddUserToOrganization")
            .WithSummary("Add user to organization")
            .WithDescription("Adds a user to the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/provision", ProvisionOrgUser)
            .RequireCallerOrganization()
            .WithName("ProvisionOrgUser")
            .WithSummary("Provision an org-scoped password user")
            .WithDescription("Creates a NEW single-org password user (no public account, no invitation) in the organization. The emailVerified bypass is gated by Platform:AllowAdminVerifiedUserCreation. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{organizationId:guid}/users", GetOrganizationUsers)
            .RequireCallerOrganization()
            .WithName("GetOrganizationUsers")
            .WithSummary("List organization users")
            .WithDescription("Lists all users in the organization.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<UserListResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/{organizationId:guid}/users/{userId:guid}", GetOrganizationUser)
            .RequireCallerOrganization()
            .WithName("GetOrganizationUser")
            .WithSummary("Get organization user")
            .WithDescription("Gets details of a specific user in the organization.")
            .RequireAuthorization("RequireOrganizationMember")
            .Produces<UserResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{organizationId:guid}/users/{userId:guid}", UpdateOrganizationUser)
            .RequireCallerOrganization()
            .WithName("UpdateOrganizationUser")
            .WithSummary("Update organization user")
            .WithDescription("Updates a user in the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{organizationId:guid}/users/{userId:guid}", RemoveUserFromOrganization)
            .RequireCallerOrganization()
            .WithName("RemoveUserFromOrganization")
            .WithSummary("Remove user from organization")
            .WithDescription("Removes a user from the organization. Requires administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // User lifecycle management
        group.MapPost("/{organizationId:guid}/users/{userId:guid}/unlock", UnlockUser)
            .RequireCallerOrganization()
            .WithName("UnlockUser")
            .WithSummary("Unlock a locked user account")
            .WithDescription("Resets the failed login counter and removes lockout for a user account.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/{userId:guid}/suspend", SuspendUser)
            .RequireCallerOrganization()
            .WithName("SuspendUser")
            .WithSummary("Suspend a user account")
            .WithDescription("Suspends a user account, preventing authentication. Active sessions are invalidated.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/{userId:guid}/reactivate", ReactivateUser)
            .RequireCallerOrganization()
            .WithName("ReactivateUser")
            .WithSummary("Reactivate a suspended user account")
            .WithDescription("Reactivates a previously suspended user account.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{organizationId:guid}/users/{userId:guid}/verify-email", AdminVerifyEmail)
            .RequireCallerOrganization()
            .WithName("AdminVerifyEmail")
            .WithSummary("Admin override to mark user email as verified")
            .WithDescription("Allows an organisation administrator to mark a user's email as verified without requiring the email verification loop. Sets EmailVerified=true, clears verification token, records audit event.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{organizationId:guid}/users/{userId:guid}/role", ChangeUserRole)
            .RequireCallerOrganization()
            .WithName("ChangeUserRole")
            .WithSummary("Change a user's role")
            .WithDescription("Changes a user's role. Cannot target SystemAdmin users or assign SystemAdmin role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Feature 060: Organization recovery config endpoints
        group.MapPost("/{orgId:guid}/recovery-config", CreateOrgRecoveryConfig)
            .RequireCallerOrganization()
            .WithName("CreateOrgRecoveryConfig")
            .WithSummary("Configure organization recovery key pair")
            .WithDescription("Sets the organization's ED25519 recovery public key for wrapping wallet recovery keys. "
                + "Requires Administrator role.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{orgId:guid}/recovery-config", GetOrgRecoveryConfig)
            .RequireCallerOrganization()
            .WithName("GetOrgRecoveryConfig")
            .WithSummary("Get organization recovery configuration")
            .WithDescription("Returns the organization's recovery key configuration status. Requires org membership.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // Feature 060: Org recovery config handlers

    private static async Task<IResult> CreateOrgRecoveryConfig(
        Guid orgId,
        OrgRecoveryConfigRequest request,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return TypedResults.Unauthorized();

        // Check if config already exists
        var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(dbContext.OrgRecoveryConfigs.Where(c => c.OrganizationId == orgId), cancellationToken);
        if (existing is not null)
            return TypedResults.Conflict(new { error = "Recovery config already exists. Use PUT to rotate." });

        var config = new OrgRecoveryConfig
        {
            OrganizationId = orgId,
            RecoveryPublicKey = request.RecoveryPublicKey,
            RecoveryKeyId = $"org-recovery:{orgId}:{Guid.NewGuid():N}",
            CreatedBy = userId
        };

        dbContext.OrgRecoveryConfigs.Add(config);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/organizations/{orgId}/recovery-config", new
        {
            recoveryKeyId = config.RecoveryKeyId,
            createdAt = config.CreatedAt
        });
    }

    private static async Task<IResult> GetOrgRecoveryConfig(
        Guid orgId,
        TenantDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var config = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(dbContext.OrgRecoveryConfigs.Where(c => c.OrganizationId == orgId), cancellationToken);

        if (config is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new
        {
            recoveryKeyId = config.RecoveryKeyId,
            hasRecoveryKey = true,
            createdAt = config.CreatedAt,
            rotatedAt = config.RotatedAt
        });
    }

    private static async Task<Results<Ok, NotFound>> UnlockUser(
        Guid organizationId,
        Guid userId,
        IIdentityRepository identityRepository,
        IPlatformUserService platformUserService,
        TenantDbContext dbContext,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var targetUser = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (targetUser == null || targetUser.OrganizationId != organizationId)
        {
            return TypedResults.NotFound();
        }

        if (targetUser.Status == IdentityStatus.Suspended)
        {
            targetUser.Status = IdentityStatus.Active;
        }
        await identityRepository.UpdateUserAsync(targetUser, cancellationToken);

        // Clear the brute-force lockout state, which lives on PlatformUser (the cross-org identity
        // anchor) rather than on the org-scoped UserIdentity. This endpoint used to reactivate a
        // Suspended status and nothing else, on the grounds that lockout "will be handled by
        // PlatformUser in a future task" — but that task has since landed
        // (PlatformUserService.ValidatePasswordAsync enforces tiered thresholds and sets
        // FailedLoginCount / LockedUntil / LockedPermanently). So an admin unlocking a
        // brute-force-locked account cleared nothing, while the audit entry below recorded
        // AccountUnlockedByAdmin as a success — the log asserted an unlock that had not happened.
        //
        // Not found is tolerated rather than fatal: a UserIdentity whose PlatformUser is missing is
        // already an inconsistency, and the status reactivation above is still worth keeping.
        var platformUser = await platformUserService.GetByIdAsync(targetUser.PlatformUserId, cancellationToken);
        if (platformUser is not null)
        {
            platformUser.FailedLoginCount = 0;
            platformUser.LockedUntil = null;
            platformUser.LockedPermanently = false;
            await platformUserService.UpdateAsync(platformUser, cancellationToken);
        }

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

    private static async Task<Results<NoContent, BadRequest<ProblemDetails>, NotFound>> AdminVerifyEmail(
        Guid organizationId,
        Guid userId,
        IOrganizationService organizationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        try
        {
            var adminUserId = GetUserId(user);
            var wasVerified = await organizationService.AdminVerifyEmailAsync(
                organizationId, userId, adminUserId, cancellationToken);

            if (!wasVerified)
            {
                return TypedResults.BadRequest(new ProblemDetails
                {
                    Title = "Already Verified",
                    Detail = "User's email is already verified.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            return TypedResults.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static async Task<Results<Ok, ValidationProblem, NotFound>> ChangeUserRole(
        Guid organizationId,
        Guid userId,
        ChangeUserRoleRequest request,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        ITenantMembershipInboxWriter membershipInbox,
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

        // Feature 118 — drop a "your role in {org} changed" inbox entry for the affected user.
        // Only fires when the target identity is linked to a PlatformUser (PlatformUserId != Empty).
        // Writer is fail-safe (try/log/swallow internally) — an inbox failure must never
        // roll back the just-committed role change.
        if (targetUser.PlatformUserId != Guid.Empty)
        {
            await membershipInbox.WriteOrgMembershipRoleChangedAsync(
                targetUser.PlatformUserId,
                organizationId,
                previousRole,
                request.Role.ToString(),
                cancellationToken).ConfigureAwait(false);
        }

        return TypedResults.Ok();
    }

    /// <summary>Records the wallet an organisation's own admin created (#1525).</summary>
    private static async Task<IResult> LinkOrganizationWallet(
        Guid organizationId,
        LinkOrganizationWalletRequest request,
        IOrganizationService organizationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        // Same-org check, done here rather than via RequireCallerOrganization because that gate
        // exempts platform SystemAdmins and this endpoint must not honour that exemption.
        var callerOrgId = user.FindFirstValue(TokenClaimConstants.OrgId)
            ?? user.FindFirstValue("organization_id");

        if (!Guid.TryParse(callerOrgId, out var callerOrg) || callerOrg != organizationId)
        {
            return Results.Problem(
                title: "Not your organisation",
                detail: "An organisation's signing wallet must be created by an administrator of that "
                      + "organisation. Its recovery phrase is shown once and never stored, so whoever "
                      + "creates it is the only person who will ever hold it — which makes this the "
                      + "organisation's own step, not one the platform can take on its behalf.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request?.WalletAddress))
        {
            return Results.Problem(
                title: "walletAddress is required",
                detail: "Create the wallet first via POST /api/v1/wallets with this organisation as "
                      + "owner, record the recovery phrase it returns, then supply its address here.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var updated = await organizationService.LinkOrganizationWalletAsync(
                organizationId, request.WalletAddress, cancellationToken);

            return updated is null
                ? Results.NotFound(new { error = $"Organization {organizationId} not found" })
                : Results.Ok(updated);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already has a wallet", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(title: "Organisation already has a wallet", detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(title: "Wallet cannot be linked", detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
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

    private static async Task<Results<Created<UserResponse>, NotFound, ValidationProblem>> ProvisionOrgUser(
        Guid organizationId,
        ProvisionOrgUserRequest request,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await organizationService.ProvisionOrgUserAsync(
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
        bool? emailVerified = null,
        string? provisionedVia = null,
        bool includePending = false,
        CancellationToken cancellationToken = default)
    {
        var response = await organizationService.GetOrganizationUsersAsync(
            organizationId, includeInactive, emailVerified, provisionedVia,
            includePending, cancellationToken);
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

/// <summary>Body of <c>POST /api/organizations/{organizationId}/wallet</c> (#1525).</summary>
/// <param name="WalletAddress">
/// Address of the wallet the org admin created against the Wallet Service, with this organisation
/// as owner. Only the address travels here — the recovery phrase never leaves the admin.
/// </param>
public sealed record LinkOrganizationWalletRequest(string WalletAddress);
