// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Provisions new users or matches returning users after OIDC authentication.
/// Matches by ExternalIdpSubject (NOT email) to handle email changes at the IDP.
/// New users are auto-provisioned with Member role and ProvisionedVia=Oidc.
/// </summary>
public class OidcProvisioningService : IOidcProvisioningService
{
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<OidcProvisioningService> _logger;

    public OidcProvisioningService(
        TenantDbContext dbContext,
        ILogger<OidcProvisioningService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(UserIdentity User, bool IsFirstLogin)> ProvisionOrMatchUserAsync(
        Guid orgId, OidcUserClaims claims, CancellationToken cancellationToken)
    {
        // Match returning user by ExternalIdpSubject within the organization
        var existingUser = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(
                u => u.OrganizationId == orgId && u.ExternalIdpSubject == claims.Subject,
                cancellationToken);

        if (existingUser is not null)
        {
            // Returning user — update LastLoginAt
            existingUser.LastLoginAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Returning OIDC user matched: {UserId} in org {OrgId}",
                existingUser.Id, orgId);

            return (existingUser, false);
        }

        // New user — auto-provision with Member role
        var email = ResolveEmail(claims);
        var displayName = ResolveDisplayName(claims);

        var newUser = new UserIdentity
        {
            OrganizationId = orgId,
            ExternalIdpSubject = claims.Subject,
            Email = email ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Roles = [UserRole.Member],
            ProvisionedVia = ProvisioningMethod.Oidc,
            Status = IdentityStatus.Active,
            EmailVerified = claims.EmailVerified,
            EmailVerifiedAt = claims.EmailVerified ? DateTimeOffset.UtcNow : null,
            ProfileCompleted = !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(displayName),
            LastLoginAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.UserIdentities.Add(newUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Auto-provisioned new OIDC user {UserId} in org {OrgId} with subject {Subject}",
            newUser.Id, orgId, claims.Subject);

        return (newUser, true);
    }

    /// <inheritdoc />
    public async Task<bool> CheckDomainRestrictionsAsync(
        Guid orgId, string email, CancellationToken cancellationToken)
    {
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);

        if (org is null)
        {
            _logger.LogWarning("Organization {OrgId} not found for domain restriction check", orgId);
            return false;
        }

        // No restrictions if AllowedEmailDomains is empty
        if (org.AllowedEmailDomains.Length == 0)
            return true;

        // Extract domain from email
        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0)
            return false;

        var emailDomain = email[(atIndex + 1)..].ToLowerInvariant();
        return org.AllowedEmailDomains
            .Any(d => string.Equals(d, emailDomain, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public Task<bool> DetermineProfileCompletionAsync(UserIdentity user)
    {
        var isIncomplete = string.IsNullOrWhiteSpace(user.Email)
            || string.IsNullOrWhiteSpace(user.DisplayName);

        return Task.FromResult(isIncomplete);
    }

    #region Claim Resolution Helpers

    /// <summary>
    /// Resolves the user's email from OIDC claims with fallback order:
    /// email → preferred_username → upn.
    /// </summary>
    private static string? ResolveEmail(OidcUserClaims claims)
    {
        if (!string.IsNullOrWhiteSpace(claims.Email))
            return claims.Email;

        if (!string.IsNullOrWhiteSpace(claims.PreferredUsername) && claims.PreferredUsername.Contains('@'))
            return claims.PreferredUsername;

        if (!string.IsNullOrWhiteSpace(claims.Upn) && claims.Upn.Contains('@'))
            return claims.Upn;

        return null;
    }

    /// <summary>
    /// Resolves the user's display name from OIDC claims with fallback order:
    /// name → given_name + family_name → email prefix.
    /// </summary>
    private static string? ResolveDisplayName(OidcUserClaims claims)
    {
        if (!string.IsNullOrWhiteSpace(claims.DisplayName))
            return claims.DisplayName;

        if (!string.IsNullOrWhiteSpace(claims.GivenName) || !string.IsNullOrWhiteSpace(claims.FamilyName))
        {
            var parts = new[] { claims.GivenName, claims.FamilyName }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var combined = string.Join(" ", parts);
            if (!string.IsNullOrWhiteSpace(combined))
                return combined;
        }

        return null;
    }

    #endregion
}
