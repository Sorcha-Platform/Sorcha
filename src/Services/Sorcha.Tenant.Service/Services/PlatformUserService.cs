// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly LockoutConfig _lockoutConfig;
    /// <summary>Optional inbox writer (Feature 118 / US3 Phase-5 follow-up #3). Null in tests where membership inbox emission is irrelevant.</summary>
    private readonly ITenantMembershipInboxWriter? _inboxWriter;

    /// <summary>
    /// Creates a new instance of <see cref="PlatformUserService"/>.
    /// </summary>
    /// <param name="db">The tenant database context.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">Application configuration for lockout thresholds.</param>
    /// <param name="inboxWriter">Optional inbox writer for Feature 118 — fires when a user joins an org.</param>
    public PlatformUserService(
        TenantDbContext db,
        ILogger<PlatformUserService> logger,
        IConfiguration configuration,
        ITenantMembershipInboxWriter? inboxWriter = null)
    {
        _db = db;
        _logger = logger;
        _lockoutConfig = LockoutConfig.FromConfiguration(configuration);
        _inboxWriter = inboxWriter;
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

        // Feature 118 / US3 follow-up #3 — drop a "welcome to {org}" inbox entry.
        // Optional + fail-safe: writer null-checks and the writer itself catches.
        if (_inboxWriter is not null)
        {
            await _inboxWriter.WriteOrgMembershipAddedAsync(platformUserId, organizationId, role, ct).ConfigureAwait(false);
        }

        return membership;
    }

    /// <inheritdoc />
    public async Task<PasswordAuthResult> ValidatePasswordAsync(PlatformUser platformUser, string password, CancellationToken ct)
    {
        // Check permanent lockout
        if (platformUser.LockedPermanently)
        {
            _logger.LogWarning("Login attempt on permanently locked account {UserId}", platformUser.Id);
            return new PasswordAuthResult(false, IsLocked: true, IsPermanentlyLocked: true);
        }

        // Check temporary lockout
        if (platformUser.LockedUntil.HasValue && platformUser.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Login attempt on temporarily locked account {UserId} (locked until {LockedUntil})",
                platformUser.Id, platformUser.LockedUntil.Value);
            return new PasswordAuthResult(false, IsLocked: true, LockedUntil: platformUser.LockedUntil);
        }

        // No password set (social-login-only user)
        if (string.IsNullOrEmpty(platformUser.PasswordHash))
        {
            return new PasswordAuthResult(false);
        }

        // Verify BCrypt hash
        if (!BCrypt.Net.BCrypt.Verify(password, platformUser.PasswordHash))
        {
            platformUser.FailedLoginCount++;

            // Progressive lockout thresholds (configurable via Security:Lockout section)
            platformUser.LockedUntil = platformUser.FailedLoginCount switch
            {
                _ when platformUser.FailedLoginCount >= _lockoutConfig.PermanentLockThreshold => null, // Permanent lockout handled below
                _ when platformUser.FailedLoginCount >= _lockoutConfig.Tier4Threshold => DateTimeOffset.UtcNow.Add(_lockoutConfig.Tier4Duration),
                _ when platformUser.FailedLoginCount >= _lockoutConfig.Tier3Threshold => DateTimeOffset.UtcNow.Add(_lockoutConfig.Tier3Duration),
                _ when platformUser.FailedLoginCount >= _lockoutConfig.Tier2Threshold => DateTimeOffset.UtcNow.Add(_lockoutConfig.Tier2Duration),
                _ when platformUser.FailedLoginCount >= _lockoutConfig.Tier1Threshold => DateTimeOffset.UtcNow.Add(_lockoutConfig.Tier1Duration),
                _ => null
            };

            if (platformUser.FailedLoginCount >= _lockoutConfig.PermanentLockThreshold)
            {
                platformUser.LockedPermanently = true;
                _logger.LogWarning("Account {UserId} permanently locked after {Count} failed attempts",
                    platformUser.Id, platformUser.FailedLoginCount);
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Failed login attempt {Count} for user {UserId}",
                platformUser.FailedLoginCount, platformUser.Id);

            return new PasswordAuthResult(false,
                IsLocked: platformUser.LockedPermanently || platformUser.LockedUntil.HasValue,
                IsPermanentlyLocked: platformUser.LockedPermanently,
                LockedUntil: platformUser.LockedUntil);
        }

        // Success — reset lockout state
        if (platformUser.FailedLoginCount > 0)
        {
            platformUser.FailedLoginCount = 0;
            platformUser.LockedUntil = null;
            platformUser.LockedPermanently = false;
            await _db.SaveChangesAsync(ct);
        }

        platformUser.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new PasswordAuthResult(true);
    }

    /// <inheritdoc />
    public async Task<ResolveSocialUserResult> ResolveOrCreateSocialUserAsync(
        SocialAuthCallbackResult claim, bool allowCreate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var provider = claim.Provider;
        var subject = claim.Subject ?? throw new ArgumentException("claim.Subject is required", nameof(claim));
        var email = claim.Email;
        var displayName = claim.DisplayName;

        // Step 1: Returning user (provider + subject already linked).
        // No verification re-check — trust is established at link time
        // (FR-013). Refresh DisplayName from the latest claim if non-empty
        // and changed (FR-008); refresh LastUsedAt unconditionally (FR-007).
        var existingByProvider = await GetByProviderSubjectAsync(provider, subject, ct);
        if (existingByProvider is not null)
        {
            var socialLogin = await _db.PlatformSocialLogins
                .FirstAsync(s => s.Provider == provider && s.Subject == subject, ct);
            socialLogin.LastUsedAt = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(displayName)
                && !string.Equals(displayName, existingByProvider.DisplayName, StringComparison.Ordinal))
            {
                existingByProvider.DisplayName = displayName;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Social login resolved existing user {UserId} via {Provider}/{Subject}",
                existingByProvider.Id, provider, subject);

            return new ResolveSocialUserResult(existingByProvider, IsNew: false, SocialLoginRefusal.None);
        }

        // Step 2: Email-collision linking — strict-link policy gate (FR-011, FR-012).
        // Both the provider's claim AND the existing Sorcha account must be verified
        // for cross-method linking to succeed. The two refusal directions point the
        // user at different recovery paths, so distinguish them precisely:
        //   provider unverified  → ProviderUnverified ("verify with the provider")
        //   existing unverified  → ExistingUnverified ("verify your Sorcha account")
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingByEmail = await GetByEmailAsync(email, ct);
            if (existingByEmail is not null)
            {
                if (!claim.EmailVerified)
                {
                    _logger.LogWarning(
                        "Social login refused: email-collision link rejected — provider {Provider} did not assert email_verified",
                        provider);

                    return new ResolveSocialUserResult(User: null, IsNew: false, SocialLoginRefusal.ProviderUnverified);
                }

                if (!existingByEmail.EmailVerified)
                {
                    _logger.LogWarning(
                        "Social login refused: email-collision link rejected — existing user {UserId} email is not verified for {Provider}",
                        existingByEmail.Id, provider);

                    return new ResolveSocialUserResult(User: null, IsNew: false, SocialLoginRefusal.ExistingUnverified);
                }

                // Feature 168: both sides are verified — gate the auto-link behind step-up proof.
                // Return LinkRequired so the callback can mint a link-pending token and start
                // the step-up flow. No link is created here; link-confirm does that on success.
                _logger.LogInformation(
                    "Social login match {Provider}/{Subject} requires step-up linking for existing user {UserId}",
                    provider, subject, existingByEmail.Id);

                return new ResolveSocialUserResult(
                    User: null,
                    IsNew: false,
                    Refusal: SocialLoginRefusal.None,
                    LinkRequired: new LinkRequiredInfo(
                        Provider: provider,
                        Subject: subject,
                        SocialEmail: email,
                        DisplayName: displayName,
                        TargetAccountId: existingByEmail.Id));
            }
        }

        // Step 3: Genuinely new user — provider must assert email_verified=true (FR-010).
        // Refusal here means no PlatformUser is created at all.

        // Login-only surfaces (e.g. the citizen wallet PWA) must not create an
        // account for an unknown social identity — refuse instead of signing up.
        if (!allowCreate)
        {
            _logger.LogInformation(
                "Social login refused: no existing account for {Provider}/{Subject} and creation is disabled (login-only surface)",
                provider, subject);
            return new ResolveSocialUserResult(User: null, IsNew: false, SocialLoginRefusal.NoExistingAccount);
        }

        if (!claim.EmailVerified)
        {
            _logger.LogWarning(
                "Social login refused: provider {Provider} did not assert email_verified for {Subject}",
                provider, subject);

            return new ResolveSocialUserResult(User: null, IsNew: false, SocialLoginRefusal.ProviderUnverified);
        }

        var resolvedDisplayName = displayName ?? email?.Split('@')[0] ?? "User";
        var newUser = await CreateAsync(email ?? $"{provider}_{subject}@noemail.local", resolvedDisplayName, null, ct);
        await LinkSocialLoginAsync(newUser.Id, provider, subject, email, displayName, ct);

        // Mark email as verified — provider asserted it and we trust that assertion.
        if (!string.IsNullOrWhiteSpace(email))
        {
            newUser.EmailVerified = true;
            newUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Social login created new user {UserId} via {Provider}/{Subject}",
            newUser.Id, provider, subject);

        return new ResolveSocialUserResult(newUser, IsNew: true, SocialLoginRefusal.None);
    }
}

/// <summary>
/// Progressive lockout configuration. Reads from Security:Lockout config section
/// with production-safe defaults. Development environments can override to higher
/// thresholds to avoid lockout during automated testing.
/// </summary>
public class LockoutConfig
{
    /// <summary>Failed attempts before Tier 1 lockout. Default: 5.</summary>
    public int Tier1Threshold { get; init; } = 5;

    /// <summary>Tier 1 lockout duration. Default: 15 minutes.</summary>
    public TimeSpan Tier1Duration { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Failed attempts before Tier 2 lockout. Default: 10.</summary>
    public int Tier2Threshold { get; init; } = 10;

    /// <summary>Tier 2 lockout duration. Default: 30 minutes.</summary>
    public TimeSpan Tier2Duration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Failed attempts before Tier 3 lockout. Default: 15.</summary>
    public int Tier3Threshold { get; init; } = 15;

    /// <summary>Tier 3 lockout duration. Default: 1 hour.</summary>
    public TimeSpan Tier3Duration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Failed attempts before Tier 4 lockout. Default: 20.</summary>
    public int Tier4Threshold { get; init; } = 20;

    /// <summary>Tier 4 lockout duration. Default: 4 hours.</summary>
    public TimeSpan Tier4Duration { get; init; } = TimeSpan.FromHours(4);

    /// <summary>Failed attempts before permanent lockout. Default: 25.</summary>
    public int PermanentLockThreshold { get; init; } = 25;

    /// <summary>
    /// Reads lockout configuration from the Security:Lockout config section.
    /// Falls back to production-safe defaults if section is missing.
    /// </summary>
    public static LockoutConfig FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Security:Lockout");
        if (!section.Exists())
            return new LockoutConfig();

        return new LockoutConfig
        {
            Tier1Threshold = section.GetValue("Tier1Threshold", 5),
            Tier1Duration = section.GetValue("Tier1Duration", TimeSpan.FromMinutes(15)),
            Tier2Threshold = section.GetValue("Tier2Threshold", 10),
            Tier2Duration = section.GetValue("Tier2Duration", TimeSpan.FromMinutes(30)),
            Tier3Threshold = section.GetValue("Tier3Threshold", 15),
            Tier3Duration = section.GetValue("Tier3Duration", TimeSpan.FromHours(1)),
            Tier4Threshold = section.GetValue("Tier4Threshold", 20),
            Tier4Duration = section.GetValue("Tier4Duration", TimeSpan.FromHours(4)),
            PermanentLockThreshold = section.GetValue("PermanentLockThreshold", 25),
        };
    }
}
