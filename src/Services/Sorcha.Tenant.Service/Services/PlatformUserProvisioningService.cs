// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a platform user provisioning attempt.
/// </summary>
/// <param name="Success">Whether provisioning succeeded.</param>
/// <param name="Response">The response DTO (if successful).</param>
/// <param name="Error">General error message (if failed).</param>
/// <param name="ErrorStatusCode">HTTP status code hint for the error (400, 404, 409).</param>
/// <param name="ValidationErrors">Field-level validation errors (if any).</param>
public record ProvisioningResult(
    bool Success,
    AdminProvisionUserResponse? Response = null,
    string? Error = null,
    int? ErrorStatusCode = null,
    Dictionary<string, string[]>? ValidationErrors = null);

/// <summary>
/// Interface for admin-initiated platform user provisioning.
/// Creates PlatformUser + UserIdentity + PlatformUserOrgMembership atomically.
/// </summary>
public interface IPlatformUserProvisioningService
{
    /// <summary>
    /// Provisions a platform user into a specific organisation.
    /// Creates or reuses PlatformUser, creates UserIdentity and OrgMembership.
    /// </summary>
    /// <param name="request">The provisioning request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Provisioning result with response or error details.</returns>
    Task<ProvisioningResult> ProvisionUserAsync(AdminProvisionUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resets a platform user's password. Validates against NIST policy.
    /// </summary>
    /// <param name="userId">PlatformUser ID.</param>
    /// <param name="newPassword">New password (NIST policy enforced).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success or error details.</returns>
    Task<ProvisioningResult> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);
}

/// <summary>
/// Handles admin-initiated platform user provisioning. Creates PlatformUser (or reuses
/// by email) + UserIdentity + PlatformUserOrgMembership atomically. Follows the same
/// pattern as <see cref="RegistrationService.RegisterAsync"/> but bypasses self-registration
/// checks and supports role selection, password hashing, and email verification skip.
/// </summary>
public class PlatformUserProvisioningService : IPlatformUserProvisioningService
{
    private readonly TenantDbContext _dbContext;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly ILogger<PlatformUserProvisioningService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformUserProvisioningService"/> class.
    /// </summary>
    public PlatformUserProvisioningService(
        TenantDbContext dbContext,
        IPasswordPolicyService passwordPolicyService,
        ILogger<PlatformUserProvisioningService> logger)
    {
        _dbContext = dbContext;
        _passwordPolicyService = passwordPolicyService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProvisioningResult> ProvisionUserAsync(
        AdminProvisionUserRequest request, CancellationToken ct = default)
    {
        // 1. Validate organisation exists
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, ct);

        if (org is null)
        {
            return new ProvisioningResult(false,
                Error: "Organization not found.",
                ErrorStatusCode: 404);
        }

        // 2. Parse and validate role
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
        {
            return new ProvisioningResult(false,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["Role"] = [$"Invalid role '{request.Role}'. Valid roles: {string.Join(", ", Enum.GetNames<UserRole>())}"]
                },
                ErrorStatusCode: 400);
        }

        // 3. Validate password against NIST policy (if provided)
        if (!string.IsNullOrEmpty(request.Password))
        {
            var passwordResult = await _passwordPolicyService.ValidateAsync(request.Password, ct);
            if (!passwordResult.IsValid)
            {
                return new ProvisioningResult(false,
                    ValidationErrors: new Dictionary<string, string[]>
                    {
                        ["Password"] = passwordResult.Errors.ToArray()
                    },
                    ErrorStatusCode: 400);
            }
        }

        // 4. Check for existing UserIdentity in this org (duplicate check)
        var existingIdentity = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.OrganizationId == request.OrganizationId, ct);

        if (existingIdentity is not null)
        {
            return new ProvisioningResult(false,
                Error: "User already exists in this organization.",
                ErrorStatusCode: 409);
        }

        // 5. Create or find PlatformUser by email
        var isExistingPlatformUser = false;
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Email == request.Email, ct);

        if (platformUser is not null)
        {
            isExistingPlatformUser = true;
        }
        else
        {
            platformUser = new PlatformUser
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                DisplayName = request.DisplayName,
                PasswordHash = !string.IsNullOrEmpty(request.Password)
                    ? BCrypt.Net.BCrypt.HashPassword(request.Password)
                    : null,
                Status = PlatformUserStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Set email verified if requested
            if (request.SkipEmailVerification)
            {
                platformUser.EmailVerified = true;
                platformUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
            }

            _dbContext.PlatformUsers.Add(platformUser);
        }

        // If existing platform user and skipEmailVerification, update verification status
        if (isExistingPlatformUser && request.SkipEmailVerification && !platformUser.EmailVerified)
        {
            platformUser.EmailVerified = true;
            platformUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        // 6. Create UserIdentity (org-scoped)
        var userIdentity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            PlatformUserId = platformUser.Id,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Status = IdentityStatus.Active,
            Roles = [role],
            ProvisionedVia = ProvisioningMethod.AdminCreated,
            ProfileCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserIdentities.Add(userIdentity);

        // 7. Create PlatformUserOrgMembership
        _dbContext.Set<PlatformUserOrgMembership>().Add(new PlatformUserOrgMembership
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUser.Id,
            OrganizationId = request.OrganizationId,
            Role = role.ToString(),
            JoinedAt = DateTimeOffset.UtcNow
        });

        // 8. Log audit event
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EventType = AuditEventType.UserAddedToOrganization,
            IdentityId = userIdentity.Id,
            OrganizationId = request.OrganizationId,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["provisionedVia"] = "AdminProvisioned",
                ["role"] = role.ToString(),
                ["isExistingPlatformUser"] = isExistingPlatformUser
            }
        });

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Admin provisioned user {Email} in org {OrgId} (UserId: {UserId}, Role: {Role}, Existing: {IsExisting})",
            request.Email, request.OrganizationId, userIdentity.Id, role, isExistingPlatformUser);

        return new ProvisioningResult(true,
            Response: new AdminProvisionUserResponse
            {
                UserId = platformUser.Id,
                UserIdentityId = userIdentity.Id,
                Email = request.Email,
                DisplayName = request.DisplayName,
                OrganizationId = request.OrganizationId,
                OrganizationName = org.Name,
                Role = role.ToString(),
                EmailVerified = platformUser.EmailVerified,
                IsExistingPlatformUser = isExistingPlatformUser
            });
    }

    /// <inheritdoc />
    public async Task<ProvisioningResult> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            return new ProvisioningResult(false, Error: "Password is required");

        if (newPassword.Length < 12)
            return new ProvisioningResult(false, Error: "Password must be at least 12 characters");

        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (platformUser == null)
            return new ProvisioningResult(false, Error: "Platform user not found", ErrorStatusCode: 404);

        // Validate password against NIST policy
        var policyResult = await _passwordPolicyService.ValidateAsync(newPassword, ct);
        if (!policyResult.IsValid)
            return new ProvisioningResult(false, Error: $"Password policy violation: {string.Join(", ", policyResult.Errors)}");

        platformUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Admin reset password for user {UserId}", userId);

        return new ProvisioningResult(true);
    }
}
