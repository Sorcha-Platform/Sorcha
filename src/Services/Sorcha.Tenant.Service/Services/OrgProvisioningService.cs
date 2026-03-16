// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Atomically provisions new private organisations with full rollback on failure.
/// Handles validation (email verified, org quota, subdomain), entity creation
/// (Organisation + admin UserIdentity + PlatformUserOrgMembership), and quota tracking.
/// </summary>
public class OrgProvisioningService : IOrgProvisioningService
{
    private readonly TenantDbContext _db;
    private readonly IOrganizationService _orgService;
    private readonly IPlatformSettingsService _settingsService;
    private readonly IInvitationService _invitationService;
    private readonly ILogger<OrgProvisioningService> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="OrgProvisioningService"/>.
    /// </summary>
    public OrgProvisioningService(
        TenantDbContext db,
        IOrganizationService orgService,
        IPlatformSettingsService settingsService,
        IInvitationService invitationService,
        ILogger<OrgProvisioningService> logger)
    {
        _db = db;
        _orgService = orgService;
        _settingsService = settingsService;
        _invitationService = invitationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgProvisioningResult?> ValidateAsync(
        Guid platformUserId, ProvisionOrgRequest request, CancellationToken ct)
    {
        // 1. Validate request fields
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 100)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = "Organisation name must be between 3 and 100 characters.",
                ErrorCode = "InvalidName"
            };
        }

        if (!string.IsNullOrEmpty(request.Description) && request.Description.Length > 500)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = "Description cannot exceed 500 characters.",
                ErrorCode = "InvalidDescription"
            };
        }

        // 2. Validate subdomain format and availability
        var (subdomainValid, subdomainError) = await _orgService.ValidateSubdomainAsync(request.Subdomain, ct);
        if (!subdomainValid)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = subdomainError,
                ErrorCode = "InvalidSubdomain"
            };
        }

        // 3. Check PlatformUser exists and is eligible
        var platformUser = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == platformUserId, ct);

        if (platformUser is null)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = "User not found.",
                ErrorCode = "UserNotFound"
            };
        }

        if (platformUser.Status != PlatformUserStatus.Active)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = "User account is not active.",
                ErrorCode = "UserInactive"
            };
        }

        // 4. Check email is verified
        if (!platformUser.EmailVerified)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = "Email must be verified before creating an organisation.",
                ErrorCode = "EmailNotVerified"
            };
        }

        // 5. Check org creation quota
        var settings = await _settingsService.GetAsync(ct);
        if (platformUser.CreatedOrgsCount >= settings.MaxOrgsPerUser)
        {
            return new OrgProvisioningResult
            {
                Success = false,
                Error = $"You have reached the maximum of {settings.MaxOrgsPerUser} organisation(s). Contact a platform administrator to increase your limit.",
                ErrorCode = "OrgLimitReached"
            };
        }

        return null; // Valid
    }

    /// <inheritdoc />
    public async Task<OrgProvisioningResult> ProvisionAsync(
        Guid platformUserId, ProvisionOrgRequest request, CancellationToken ct)
    {
        // Run validation first
        var validationError = await ValidateAsync(platformUserId, request, ct);
        if (validationError is not null)
            return validationError;

        var platformUser = await _db.PlatformUsers
            .FirstAsync(u => u.Id == platformUserId, ct);

        // Use a strategy-based execution to ensure atomicity
        // EF Core batches all changes and commits in a single transaction
        try
        {
            // 1. Create Organisation entity
            var org = new Organization
            {
                Name = request.Name,
                Subdomain = request.Subdomain.ToLowerInvariant(),
                Status = OrganizationStatus.Active,
                OrgType = OrgType.Standard,
                IsPlatformOrg = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Organizations.Add(org);

            // 2. Create admin UserIdentity in the new org
            var adminIdentity = new UserIdentity
            {
                OrganizationId = org.Id,
                PlatformUserId = platformUserId,
                Email = platformUser.Email,
                DisplayName = platformUser.DisplayName,
                Roles = [UserRole.Administrator, UserRole.Designer, UserRole.Member],
                Status = IdentityStatus.Active,
                ProvisionedVia = ProvisioningMethod.Local,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.UserIdentities.Add(adminIdentity);

            // Set the creator identity ID on the org
            org.CreatorIdentityId = adminIdentity.Id;

            // 3. Create PlatformUserOrgMembership
            var membership = new PlatformUserOrgMembership
            {
                PlatformUserId = platformUserId,
                OrganizationId = org.Id,
                Role = UserRole.Administrator.ToString(),
                JoinedAt = DateTimeOffset.UtcNow
            };
            _db.PlatformUserOrgMemberships.Add(membership);

            // 4. Increment CreatedOrgsCount
            platformUser.CreatedOrgsCount++;

            // 5. Audit log
            _db.AuditLogEntries.Add(new AuditLogEntry
            {
                EventType = AuditEventType.OrganizationCreated,
                IdentityId = platformUserId,
                OrganizationId = org.Id,
                Timestamp = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["orgName"] = request.Name,
                    ["subdomain"] = request.Subdomain,
                    ["createdOrgsCount"] = platformUser.CreatedOrgsCount
                }
            });

            // 6. Single SaveChangesAsync — atomic commit
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Organisation provisioned: {OrgId} ({Subdomain}) by platform user {UserId} (total orgs: {Count})",
                org.Id, org.Subdomain, platformUserId, platformUser.CreatedOrgsCount);

            return new OrgProvisioningResult
            {
                Success = true,
                OrganizationId = org.Id,
                OrganizationName = org.Name,
                Subdomain = org.Subdomain
            };
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogWarning(ex, "Org provisioning failed due to unique constraint for subdomain {Subdomain}", request.Subdomain);
            return new OrgProvisioningResult
            {
                Success = false,
                Error = $"Subdomain '{request.Subdomain}' is already taken.",
                ErrorCode = "SubdomainConflict"
            };
        }
    }

    /// <inheritdoc />
    public async Task<AdminProvisionResult> AdminProvisionAsync(
        Guid adminPlatformUserId, string name, string subdomain,
        string? description, string adminEmail, UserRole role, CancellationToken ct)
    {
        // Reject SystemAdmin role assignment (only valid in system admin org bootstrap)
        if (role == UserRole.SystemAdmin)
        {
            return new AdminProvisionResult
            {
                Success = false,
                Error = "SystemAdmin role can only be assigned in the system admin organisation.",
                ErrorCode = "invalid_role"
            };
        }

        // 1. Validate org fields (name, subdomain)
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3 || name.Length > 100)
        {
            return new AdminProvisionResult
            {
                Success = false,
                Error = "Organisation name must be between 3 and 100 characters.",
                ErrorCode = "InvalidName"
            };
        }

        if (!string.IsNullOrEmpty(description) && description.Length > 500)
        {
            return new AdminProvisionResult
            {
                Success = false,
                Error = "Description cannot exceed 500 characters.",
                ErrorCode = "InvalidDescription"
            };
        }

        var (subdomainValid, subdomainError) = await _orgService.ValidateSubdomainAsync(subdomain, ct);
        if (!subdomainValid)
        {
            return new AdminProvisionResult
            {
                Success = false,
                Error = subdomainError,
                ErrorCode = "InvalidSubdomain"
            };
        }

        // 2. Validate admin email format
        if (string.IsNullOrWhiteSpace(adminEmail) || !adminEmail.Contains('@') || adminEmail.Length > 320)
        {
            return new AdminProvisionResult
            {
                Success = false,
                Error = "A valid admin email address is required.",
                ErrorCode = "InvalidAdminEmail"
            };
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 3. Create the organisation
            var org = new Organization
            {
                Name = name,
                Subdomain = subdomain.ToLowerInvariant(),
                Status = OrganizationStatus.Active,
                OrgType = OrgType.Standard,
                IsPlatformOrg = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Organizations.Add(org);

            // 4. Check if admin email matches an existing PlatformUser
            var existingUser = await _db.PlatformUsers
                .FirstOrDefaultAsync(u => u.Email.ToLower() == adminEmail.ToLower(), ct);

            bool adminDirectlyAdded = false;
            Guid? invitationId = null;

            if (existingUser is not null)
            {
                // Build role set: assigned role + implied subordinate roles
                var roles = BuildRoleSet(role);

                // Directly create UserIdentity + PlatformUserOrgMembership
                var adminIdentity = new UserIdentity
                {
                    OrganizationId = org.Id,
                    PlatformUserId = existingUser.Id,
                    Email = existingUser.Email,
                    DisplayName = existingUser.DisplayName,
                    Roles = roles,
                    Status = IdentityStatus.Active,
                    ProvisionedVia = ProvisioningMethod.AdminCreated,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.UserIdentities.Add(adminIdentity);

                org.CreatorIdentityId = adminIdentity.Id;

                _db.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
                {
                    PlatformUserId = existingUser.Id,
                    OrganizationId = org.Id,
                    Role = role.ToString(),
                    JoinedAt = DateTimeOffset.UtcNow
                });

                adminDirectlyAdded = true;

                _logger.LogInformation(
                    "Admin org creation: existing user {UserId} directly added as admin to org {OrgId}",
                    existingUser.Id, org.Id);
            }

            // 5. Audit log
            _db.AuditLogEntries.Add(new AuditLogEntry
            {
                EventType = AuditEventType.OrganizationCreated,
                IdentityId = adminPlatformUserId,
                OrganizationId = org.Id,
                Timestamp = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["orgName"] = name,
                    ["subdomain"] = subdomain,
                    ["adminEmail"] = adminEmail,
                    ["assignedRole"] = role.ToString(),
                    ["adminDirectlyAdded"] = adminDirectlyAdded,
                    ["createdBySystemAdmin"] = true
                }
            });

            // 6. Save org + optional identity within transaction
            await _db.SaveChangesAsync(ct);

            // 7. If admin email is new, create invitation within the same transaction
            //    (InvitationService requires the org to exist — it does within this transaction)
            if (!adminDirectlyAdded)
            {
                var invitationResponse = await _invitationService.CreateInvitationAsync(
                    org.Id,
                    new Models.Dtos.CreateInvitationRequest
                    {
                        Email = adminEmail,
                        Role = role,
                        ExpiryDays = 14
                    },
                    adminPlatformUserId,
                    ct);

                invitationId = invitationResponse.Id;

                _logger.LogInformation(
                    "Admin org creation: invitation {InvitationId} sent to {Email} for org {OrgId}",
                    invitationId, adminEmail, org.Id);
            }

            // 8. Commit the full transaction (org + identity/invitation)
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Organisation provisioned by system admin: {OrgId} ({Subdomain}), admin={AdminEmail}, direct={Direct}",
                org.Id, org.Subdomain, adminEmail, adminDirectlyAdded);

            return new AdminProvisionResult
            {
                Success = true,
                OrganizationId = org.Id,
                OrganizationName = org.Name,
                Subdomain = org.Subdomain,
                AdminDirectlyAdded = adminDirectlyAdded,
                InvitationId = invitationId
            };
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogWarning(ex, "Admin org provisioning failed due to unique constraint for subdomain {Subdomain}", subdomain);
            return new AdminProvisionResult
            {
                Success = false,
                Error = $"Subdomain '{subdomain}' is already taken.",
                ErrorCode = "SubdomainConflict"
            };
        }
    }

    /// <summary>
    /// Builds a role set from a primary role, including implied subordinate roles.
    /// Administrator implies Designer + Member. Designer implies Member. Auditor is standalone + Member.
    /// </summary>
    private static UserRole[] BuildRoleSet(UserRole primaryRole) => primaryRole switch
    {
        UserRole.Administrator => [UserRole.Administrator, UserRole.Designer, UserRole.Member],
        UserRole.Designer => [UserRole.Designer, UserRole.Member],
        UserRole.Auditor => [UserRole.Auditor, UserRole.Member],
        UserRole.Member => [UserRole.Member],
        _ => [primaryRole, UserRole.Member]
    };
}
