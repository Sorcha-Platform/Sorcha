// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Handles user self-registration with email/password. Validates organization
/// settings, password policy, email uniqueness, domain restrictions, creates
/// the user, logs an audit event, and sends verification email.
/// </summary>
public class RegistrationService : IRegistrationService
{
    private readonly TenantDbContext _dbContext;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly ILogger<RegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegistrationService"/> class.
    /// </summary>
    public RegistrationService(
        TenantDbContext dbContext,
        IPasswordPolicyService passwordPolicyService,
        IEmailVerificationService emailVerificationService,
        ILogger<RegistrationService> logger)
    {
        _dbContext = dbContext;
        _passwordPolicyService = passwordPolicyService;
        _emailVerificationService = emailVerificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegistrationResult> RegisterAsync(
        string orgSubdomain, string email, string password,
        string displayName, CancellationToken ct = default)
    {
        // Resolve organization
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Subdomain == orgSubdomain, ct);

        if (org is null)
        {
            return new RegistrationResult(false,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["orgSubdomain"] = ["Organization not found"]
                });
        }

        // Check if org allows self-registration
        if (org.OrgType != OrgType.Public || !org.SelfRegistrationEnabled)
        {
            return new RegistrationResult(false,
                Error: "Self-registration is not enabled for this organization.",
                ErrorStatusCode: 403);
        }

        // Validate password against NIST policy + HIBP breach check
        var passwordResult = await _passwordPolicyService.ValidateAsync(password, ct);
        if (!passwordResult.IsValid)
        {
            return new RegistrationResult(false,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["password"] = passwordResult.Errors.ToArray()
                });
        }

        // Check email uniqueness within the organization
        var existingUser = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Email == email && u.OrganizationId == org.Id, ct);

        if (existingUser is not null)
        {
            return new RegistrationResult(false,
                Error: "An account with this email already exists.",
                ErrorStatusCode: 409);
        }

        // Check domain restrictions
        if (org.AllowedEmailDomains is { Length: > 0 })
        {
            var emailDomain = email.Split('@').LastOrDefault();
            if (emailDomain is null || !org.AllowedEmailDomains.Contains(emailDomain, StringComparer.OrdinalIgnoreCase))
            {
                return new RegistrationResult(false,
                    Error: "Registration is restricted to specific email domains.",
                    ErrorStatusCode: 403);
            }
        }

        // Create the user
        var user = new UserIdentity
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Email = email,
            DisplayName = displayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Status = IdentityStatus.Active,
            Roles = [UserRole.Member],
            ProvisionedVia = ProvisioningMethod.Local,
            ProfileCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserIdentities.Add(user);

        // Log audit event
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EventType = AuditEventType.SelfRegistration,
            IdentityId = user.Id,
            OrganizationId = org.Id,
            Timestamp = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(ct);

        // Send verification email — wrapped in try/catch so a transient email failure
        // doesn't leave the user unable to re-request verification later.
        try
        {
            await _emailVerificationService.GenerateAndSendVerificationAsync(user, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email for user {Email} (UserId: {UserId}). " +
                "User can re-request verification from the login page.", user.Email, user.Id);
        }

        _logger.LogInformation(
            "User self-registered: {Email} in org {OrgSubdomain} (UserId: {UserId})",
            user.Email, orgSubdomain, user.Id);

        return new RegistrationResult(true,
            UserId: user.Id,
            Message: "Account created. Please check your email to verify your address.");
    }
}
