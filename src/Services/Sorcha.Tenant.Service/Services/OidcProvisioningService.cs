// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Thrown when an OIDC login is refused because the IdP did not assert <c>email_verified</c>. The OIDC
/// provisioning path matches and creates users by email, so an unverified email cannot be trusted as an
/// identity key (see <see cref="OidcProvisioningService.ProvisionOrMatchUserAsync"/> / #1212). Callers
/// render this as a clean auth failure, not a 500.
/// </summary>
public sealed class OidcEmailNotVerifiedException : Exception
{
    /// <summary>The user-facing refusal message.</summary>
    public const string UserFacingMessage =
        "Your identity provider has not verified your email address, so sign-in cannot continue. "
        + "Verify your email with your provider and try again.";

    /// <summary>Creates the exception with the standard user-facing message.</summary>
    public OidcEmailNotVerifiedException() : base(UserFacingMessage) { }
}

/// <summary>
/// Provisions new users or matches returning users after OIDC authentication.
/// Matches by ExternalIdpSubject (NOT email) to handle email changes at the IDP.
/// New users are auto-provisioned with Member role and ProvisionedVia=Oidc.
/// </summary>
public class OidcProvisioningService : IOidcProvisioningService
{
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<OidcProvisioningService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="OidcProvisioningService"/>.
    /// </summary>
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
        // Security gate (#1212): this method matches and provisions users purely by EMAIL — the IdP
        // subject is not persisted (UserIdentity.ExternalIdpSubject was removed; subject-based matching
        // is a follow-on that routes through PlatformSocialLogin). Email is not a safe key when the IdP
        // does not assert it verified: controlling an unverified mailbox value would otherwise be enough
        // to be matched onto an existing account (takeover), or to seed an account a later verified login
        // collides with. So refuse unless email_verified is true — the same rule the social-login path
        // (ResolveOrCreateSocialUserAsync) already enforces. This gates BOTH the match and the create
        // below, and lives in the service so no caller can forget it.
        if (!claims.EmailVerified)
        {
            _logger.LogWarning(
                "OIDC login refused for org {OrgId}: the IdP did not assert email_verified for subject {Subject}",
                orgId, claims.Subject);
            throw new OidcEmailNotVerifiedException();
        }

        // TODO: ExternalIdpSubject removed from UserIdentity — matching by external subject
        // will be handled by PlatformSocialLogin in a future task. For now, match by email as fallback.
        var existingUser = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(
                u => u.OrganizationId == orgId && u.Email == claims.Email,
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
            // TODO: ExternalIdpSubject removed — external subject tracking moves to PlatformSocialLogin
            Email = email ?? string.Empty,
            DisplayName = displayName ?? string.Empty,
            Roles = [UserRole.Consumer],
            ProvisionedVia = ProvisioningMethod.Oidc,
            Status = IdentityStatus.Active,
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
