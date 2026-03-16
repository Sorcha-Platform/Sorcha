// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for platform-wide user management operations.
/// Handles PlatformUser lifecycle, social login linking, and organisation membership.
/// </summary>
public class PlatformUserService : IPlatformUserService
{
    private readonly TenantDbContext _db;
    private readonly ILogger<PlatformUserService> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="PlatformUserService"/>.
    /// </summary>
    /// <param name="db">The tenant database context.</param>
    /// <param name="logger">The logger instance.</param>
    public PlatformUserService(TenantDbContext db, ILogger<PlatformUserService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlatformUser> CreateAsync(string email, string displayName, string? passwordHash, CancellationToken ct)
    {
        var existingUser = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

        if (existingUser is not null)
        {
            throw new InvalidOperationException($"A user with email '{email}' already exists.");
        }

        var user = new PlatformUser
        {
            Email = email,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created platform user {UserId} with email {Email}", user.Id, email);

        return user;
    }

    /// <inheritdoc />
    public async Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    /// <inheritdoc />
    public async Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct)
    {
        return await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);
    }

    /// <inheritdoc />
    public async Task<PlatformUser?> GetByProviderSubjectAsync(string provider, string subject, CancellationToken ct)
    {
        var socialLogin = await _db.PlatformSocialLogins
            .Include(s => s.PlatformUser)
            .FirstOrDefaultAsync(s => s.Provider == provider && s.Subject == subject, ct);

        return socialLogin?.PlatformUser;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(PlatformUser user, CancellationToken ct)
    {
        _db.PlatformUsers.Update(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated platform user {UserId}", user.Id);
    }

    /// <inheritdoc />
    public async Task<PlatformSocialLogin> LinkSocialLoginAsync(
        Guid platformUserId,
        string provider,
        string subject,
        string? email,
        string? displayName,
        CancellationToken ct)
    {
        var existingLink = await _db.PlatformSocialLogins
            .FirstOrDefaultAsync(s => s.Provider == provider && s.Subject == subject, ct);

        if (existingLink is not null)
        {
            throw new InvalidOperationException(
                $"Social login for provider '{provider}' with subject '{subject}' is already linked to a user.");
        }

        var socialLogin = new PlatformSocialLogin
        {
            PlatformUserId = platformUserId,
            Provider = provider,
            Subject = subject,
            Email = email,
            DisplayName = displayName,
            LinkedAt = DateTimeOffset.UtcNow
        };

        _db.PlatformSocialLogins.Add(socialLogin);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Linked social login {Provider}/{Subject} to platform user {UserId}",
            provider, subject, platformUserId);

        return socialLogin;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlatformUserOrgMembership>> GetOrgMembershipsAsync(Guid platformUserId, CancellationToken ct)
    {
        return await _db.PlatformUserOrgMemberships
            .Where(m => m.PlatformUserId == platformUserId)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<PlatformUserOrgMembership> AddOrgMembershipAsync(
        Guid platformUserId,
        Guid organizationId,
        string role,
        CancellationToken ct)
    {
        var existingMembership = await _db.PlatformUserOrgMemberships
            .FirstOrDefaultAsync(m => m.PlatformUserId == platformUserId && m.OrganizationId == organizationId, ct);

        if (existingMembership is not null)
        {
            throw new InvalidOperationException(
                $"Platform user '{platformUserId}' already has a membership in organisation '{organizationId}'.");
        }

        var membership = new PlatformUserOrgMembership
        {
            PlatformUserId = platformUserId,
            OrganizationId = organizationId,
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        _db.PlatformUserOrgMemberships.Add(membership);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Added organisation membership for platform user {UserId} in org {OrgId} with role {Role}",
            platformUserId, organizationId, role);

        return membership;
    }

    /// <inheritdoc />
    public async Task<(PlatformUser User, bool IsNew)> ResolveOrCreateSocialUserAsync(
        string provider, string subject, string? email, string? displayName, CancellationToken ct)
    {
        // Step 1: Find by provider + subject (returning user)
        var existingByProvider = await GetByProviderSubjectAsync(provider, subject, ct);
        if (existingByProvider is not null)
        {
            // Update last used timestamp on the social login
            var socialLogin = await _db.PlatformSocialLogins
                .FirstAsync(s => s.Provider == provider && s.Subject == subject, ct);
            socialLogin.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Social login resolved existing user {UserId} via {Provider}/{Subject}",
                existingByProvider.Id, provider, subject);

            return (existingByProvider, false);
        }

        // Step 2: Find by email and link provider (existing user, new provider)
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingByEmail = await GetByEmailAsync(email, ct);
            if (existingByEmail is not null)
            {
                await LinkSocialLoginAsync(existingByEmail.Id, provider, subject, email, displayName, ct);

                _logger.LogInformation(
                    "Social login linked {Provider}/{Subject} to existing user {UserId} via email match",
                    provider, subject, existingByEmail.Id);

                return (existingByEmail, false);
            }
        }

        // Step 3: Create new PlatformUser + link social login
        var resolvedDisplayName = displayName ?? email?.Split('@')[0] ?? "User";
        var newUser = await CreateAsync(email ?? $"{provider}_{subject}@noemail.local", resolvedDisplayName, null, ct);
        await LinkSocialLoginAsync(newUser.Id, provider, subject, email, displayName, ct);

        // Mark email as verified for social login users (provider verified it)
        if (!string.IsNullOrWhiteSpace(email))
        {
            newUser.EmailVerified = true;
            newUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Social login created new user {UserId} via {Provider}/{Subject}",
            newUser.Id, provider, subject);

        return (newUser, true);
    }
}
