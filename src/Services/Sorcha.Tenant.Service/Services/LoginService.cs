// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Authenticates users with email/password, checking BCrypt hash,
/// 2FA requirements, and issuing JWT tokens.
/// </summary>
public class LoginService : ILoginService
{
    private readonly TenantDbContext _dbContext;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly IPasskeyService _passkeyService;
    private readonly ITokenRevocationService _revocationService;
    private readonly IPlatformUserService _platformUserService;
    private readonly IWelcomeEmailDispatcher _welcomeDispatcher;
    private readonly IVerificationChannelRegistry _channels;
    private readonly ILogger<LoginService> _logger;
    private readonly IdentityMetrics? _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginService"/> class.
    /// </summary>
    public LoginService(
        TenantDbContext dbContext,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        ITotpService totpService,
        IPasskeyService passkeyService,
        ITokenRevocationService revocationService,
        IPlatformUserService platformUserService,
        IWelcomeEmailDispatcher welcomeDispatcher,
        IVerificationChannelRegistry channels,
        ILogger<LoginService> logger,
        IdentityMetrics? metrics = null)
    {
        _metrics = metrics;
        _dbContext = dbContext;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _tokenService = tokenService;
        _totpService = totpService;
        _passkeyService = passkeyService;
        _revocationService = revocationService;
        _platformUserService = platformUserService;
        _welcomeDispatcher = welcomeDispatcher;
        _channels = channels;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string email, string password, Tier preferredTier = Tier.Platform, bool tierExplicit = false, CancellationToken ct = default)
    {
        // Rate limiting check
        if (await _revocationService.IsRateLimitedAsync(email, ct))
        {
            _logger.LogWarning("Login rate-limited for {Email}", email);
            return new LoginResult(false, Error: "Too many login attempts. Please try again later.",
                ErrorCode: LoginErrorCode.RateLimited);
        }

        try
        {
            // Authenticate against PlatformUser (cross-org identity)
            var platformUser = await _platformUserService.GetByEmailAsync(email, ct);
            if (platformUser is null || platformUser.Status != PlatformUserStatus.Active)
            {
                _logger.LogWarning("Login failed: PlatformUser not found or inactive - {Email}", email);
                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Validate password with progressive lockout
            var passwordResult = await _platformUserService.ValidatePasswordAsync(platformUser, password, ct);

            if (!passwordResult.Success)
            {
                if (passwordResult.IsPermanentlyLocked)
                {
                    _logger.LogWarning("Login failed: Account permanently locked - {Email}", email);
                    return new LoginResult(false,
                        Error: "Account is locked. Please contact an administrator.",
                        ErrorCode: LoginErrorCode.AccountLocked);
                }

                if (passwordResult.IsLocked)
                {
                    _logger.LogWarning("Login failed: Account temporarily locked - {Email}", email);
                    return new LoginResult(false,
                        Error: "Too many failed login attempts. Please try again later.",
                        ErrorCode: LoginErrorCode.AccountLocked);
                }

                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Reset failed attempts on successful password verification
            await _revocationService.ResetFailedAuthAttemptsAsync(email, ct);

            // Get all org memberships with active orgs
            var memberships = await _platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
            var orgIds = memberships.Select(m => m.OrganizationId).ToList();

            var activeOrgs = await _dbContext.Organizations
                .Where(o => orgIds.Contains(o.Id) && o.Status == OrganizationStatus.Active)
                .ToListAsync(ct);

            if (activeOrgs.Count == 0)
            {
                _logger.LogWarning("Login failed: No active org memberships for {Email}", email);
                return new LoginResult(false, Error: "No active organizations found for this account.");
            }

            // Single org — auto-select (no picker needed)
            if (activeOrgs.Count == 1)
            {
                return await IssueTokensForOrgAsync(platformUser, activeOrgs[0], memberships, preferredTier, tierExplicit, ct);
            }

            // Multiple orgs — return org list for user to pick
            var orgChoices = activeOrgs
                .Join(memberships, o => o.Id, m => m.OrganizationId, (o, m) => new OrgChoice(
                    o.Id, o.Name, o.Subdomain ?? "", m.Role))
                .ToList();

            // Issue a platform login token (reuse TOTP token infra — short-lived, scoped to platform user)
            // We use a UserIdentity ID for the token, so pick the first available identity
            var anyIdentity = await _dbContext.UserIdentities
                .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.Status == IdentityStatus.Active, ct);
            if (anyIdentity is null)
            {
                _logger.LogWarning("Login failed: No active UserIdentity for {Email}", email);
                return new LoginResult(false, Error: "No active identity found for this account.");
            }

            var platformLoginToken = await _totpService.GenerateLoginTokenAsync(anyIdentity.Id, ct);

            _logger.LogInformation("Login requires org selection for {Email} — {Count} orgs available",
                email, activeOrgs.Count);

            return new LoginResult(true, OrgSelectionRequired: true,
                AvailableOrganizations: orgChoices,
                PlatformLoginToken: platformLoginToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed with exception - {Email}", email);
            return new LoginResult(false, Error: "Invalid email or password.");
        }
    }

    /// <inheritdoc />
    public async Task<LoginResult> CompleteOrgSelectionAsync(string platformLoginToken, Guid organizationId, Tier preferredTier = Tier.Platform, bool tierExplicit = false, CancellationToken ct = default)
    {
        // Validate the platform login token
        var userIdentityId = await _totpService.ValidateLoginTokenAsync(platformLoginToken, ct);
        if (userIdentityId is null)
        {
            _logger.LogWarning("Org selection failed: invalid or expired platform login token");
            return new LoginResult(false, Error: "Login session expired. Please sign in again.");
        }

        // Resolve platform user from the identity
        var sourceIdentity = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Id == userIdentityId.Value, ct);
        if (sourceIdentity is null)
        {
            return new LoginResult(false, Error: "Invalid session.");
        }

        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == sourceIdentity.PlatformUserId, ct);
        if (platformUser is null || platformUser.Status != PlatformUserStatus.Active)
        {
            return new LoginResult(false, Error: "Account is no longer active.");
        }

        // Verify membership in chosen org
        var memberships = await _platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
        if (!memberships.Any(m => m.OrganizationId == organizationId))
        {
            _logger.LogWarning("Org selection failed: user {UserId} not a member of org {OrgId}",
                platformUser.Id, organizationId);
            return new LoginResult(false, Error: "You are not a member of this organization.");
        }

        var organization = await _organizationRepository.GetByIdAsync(organizationId, ct);
        if (organization is null || organization.Status != OrganizationStatus.Active)
        {
            return new LoginResult(false, Error: "Organization is not available.");
        }

        return await IssueTokensForOrgAsync(platformUser, organization, memberships, preferredTier, tierExplicit, ct);
    }

    /// <summary>
    /// Issues JWT tokens for a user in a specific org, handling 2FA if needed.
    /// </summary>
    private async Task<LoginResult> IssueTokensForOrgAsync(
        PlatformUser platformUser,
        Organization organization,
        IReadOnlyList<PlatformUserOrgMembership> memberships,
        Tier preferredTier,
        bool tierExplicit,
        CancellationToken ct)
    {
        // Get UserIdentity in the target org
        var user = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id
                && u.OrganizationId == organization.Id
                && u.Status == IdentityStatus.Active, ct);

        if (user is null)
        {
            _logger.LogWarning("Login failed: No active UserIdentity in org {OrgId} for platform user {PlatformUserId}",
                organization.Id, platformUser.Id);
            return new LoginResult(false, Error: "No active identity found in this organization.");
        }

        // Spec 136: resolve the preferred tier against entitlement (the tier follows the person).
        // A destination-derived preference downgrades to the entitled tier (a citizen on /app → Consumer);
        // an explicit over-request is refused (FR-008). Resolve before the 2FA branch so an explicit
        // over-request fails fast.
        var tierResolution = TierResolver.ResolvePreference(preferredTier, tierExplicit, user.Roles);
        if (!tierResolution.Allowed)
        {
            _metrics?.TierRequestRejected(tierResolution.Tier, "not_entitled");
            _logger.LogWarning("Login refused for {Email} in org {OrgId}: requested tier {Tier} not entitled",
                user.Email, organization.Id, tierResolution.Tier);
            return new LoginResult(false,
                Error: "The requested access tier is not available for this account.",
                ErrorCode: LoginErrorCode.TierNotEntitled);
        }
        var mintTier = tierResolution.Tier;

        // Check enrolled second factors. TOTP + passkeys are org-scoped / per-credential; email OTP
        // (Feature 150 US2) is account-wide and only counts when the channel is configured.
        var totpStatus = await _totpService.GetStatusAsync(user.Id, ct);
        var passkeys = await _passkeyService.GetCredentialsByOwnerAsync(platformUser.Id, ct);
        var hasActivePasskeys = passkeys.Any(p => p.Status == CredentialStatus.Active);
        var twoFactor = await _dbContext.PlatformUserTwoFactors
            .AsNoTracking()
            .Where(t => t.PlatformUserId == platformUser.Id)
            .Select(t => new { t.EmailOtpEnabled, t.SmsOtpEnabled })
            .FirstOrDefaultAsync(ct);
        var emailOtpEnabled = _channels.Resolve(ChallengeMethod.EmailOtp) is not null && (twoFactor?.EmailOtpEnabled ?? false);
        var smsOtpEnabled = _channels.Resolve(ChallengeMethod.SmsOtp) is not null && (twoFactor?.SmsOtpEnabled ?? false);

        if (totpStatus.IsEnabled || hasActivePasskeys || emailOtpEnabled || smsOtpEnabled)
        {
            var loginToken = await _totpService.GenerateLoginTokenAsync(user.Id, ct);
            // Strongest-enrolled first (passkey → totp → email → sms), with a "use another method" fallback.
            var methods = new List<string>();
            if (hasActivePasskeys) methods.Add("passkey");
            if (totpStatus.IsEnabled) methods.Add("totp");
            if (emailOtpEnabled) methods.Add("email");
            if (smsOtpEnabled) methods.Add("sms");

            // When a server-sent code is the ONLY factor, dispatch it now (email preferred over sms).
            // When a stronger factor exists the client requests a code on demand (the fallback path).
            if (!totpStatus.IsEnabled && !hasActivePasskeys)
            {
                var soleChannel = emailOtpEnabled ? ChallengeMethod.EmailOtp : smsOtpEnabled ? ChallengeMethod.SmsOtp : (ChallengeMethod?)null;
                if (soleChannel is { } ch)
                {
                    var channel = _channels.Resolve(ch);
                    if (channel is not null)
                        await channel.SendAsync(platformUser.Id, OtpPurpose.Login2Fa, ct);
                }
            }

            _logger.LogInformation("Login requires 2FA for {Email} in org {OrgId}, methods: {Methods}",
                user.Email, organization.Id, string.Join(", ", methods));

            return new LoginResult(true, TwoFactorRequired: true,
                LoginToken: loginToken, AvailableMethods: methods);
        }

        // No 2FA — issue JWT
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _identityRepository.UpdateUserAsync(user, ct);

        var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, organization, platformUser.Id, mintTier, ct);

        _logger.LogInformation("User logged in successfully - {Email} (OrgId: {OrgId}, tier: {Tier})",
            user.Email, organization.Id, mintTier);

        // Fire-and-forget welcome email if this is the user's first login and they
        // haven't been welcomed yet. Idempotent + non-throwing by design.
        await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);

        return new LoginResult(true, Tokens: tokenResponse);
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string email, string password, string orgSubdomain, Tier preferredTier = Tier.Platform, bool tierExplicit = false, CancellationToken ct = default)
    {
        // Rate limiting check
        if (await _revocationService.IsRateLimitedAsync(email, ct))
        {
            _logger.LogWarning("Login rate-limited for {Email}", email);
            return new LoginResult(false, Error: "Too many login attempts. Please try again later.",
                ErrorCode: LoginErrorCode.RateLimited);
        }

        try
        {
            // Resolve PlatformUser by email
            var platformUser = await _platformUserService.GetByEmailAsync(email, ct);
            if (platformUser is null || platformUser.Status != PlatformUserStatus.Active)
            {
                _logger.LogWarning("Subdomain login failed: PlatformUser not found or inactive - {Email}", email);
                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Validate password with progressive lockout
            var passwordResult = await _platformUserService.ValidatePasswordAsync(platformUser, password, ct);
            if (!passwordResult.Success)
            {
                if (passwordResult.IsPermanentlyLocked)
                    return new LoginResult(false, Error: "Account is locked. Please contact an administrator.",
                        ErrorCode: LoginErrorCode.AccountLocked);
                if (passwordResult.IsLocked)
                    return new LoginResult(false, Error: "Too many failed login attempts. Please try again later.",
                        ErrorCode: LoginErrorCode.AccountLocked);

                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Resolve target organization by subdomain
            var organization = await _dbContext.Organizations
                .FirstOrDefaultAsync(o => o.Subdomain == orgSubdomain, ct);
            if (organization is null)
            {
                _logger.LogWarning("Subdomain login failed: org not found - {Subdomain}", orgSubdomain);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Verify org membership
            var memberships = await _platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
            if (!memberships.Any(m => m.OrganizationId == organization.Id))
            {
                _logger.LogWarning("Subdomain login failed: user {Email} not a member of org {Subdomain}",
                    email, orgSubdomain);
                return new LoginResult(false, Error: "You are not a member of this organization.");
            }

            // Get UserIdentity in the target org
            var user = await _dbContext.UserIdentities
                .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.OrganizationId == organization.Id, ct);
            if (user is null || user.Status != IdentityStatus.Active)
            {
                _logger.LogWarning("Subdomain login failed: UserIdentity not found/inactive in org {OrgId} for {Email}",
                    organization.Id, email);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Reset failed attempts
            await _revocationService.ResetFailedAuthAttemptsAsync(email, ct);

            // Check 2FA
            var totpStatus = await _totpService.GetStatusAsync(user.Id, ct);
            var passkeys = await _passkeyService.GetCredentialsByOwnerAsync(platformUser.Id, ct);
            var hasActivePasskeys = passkeys.Any(p => p.Status == CredentialStatus.Active);

            if (totpStatus.IsEnabled || hasActivePasskeys)
            {
                var loginToken = await _totpService.GenerateLoginTokenAsync(user.Id, ct);
                var methods = new List<string>();
                if (totpStatus.IsEnabled) methods.Add("totp");
                if (hasActivePasskeys) methods.Add("passkey");

                return new LoginResult(true, TwoFactorRequired: true,
                    LoginToken: loginToken, AvailableMethods: methods);
            }

            // Spec 136: resolve preferred tier against entitlement (downgrade derived / refuse explicit over-request).
            var tierResolution = TierResolver.ResolvePreference(preferredTier, tierExplicit, user.Roles);
            if (!tierResolution.Allowed)
            {
                _metrics?.TierRequestRejected(tierResolution.Tier, "not_entitled");
                _logger.LogWarning("Subdomain login refused for {Email} in {Subdomain}: requested tier {Tier} not entitled",
                    email, orgSubdomain, tierResolution.Tier);
                return new LoginResult(false,
                    Error: "The requested access tier is not available for this account.",
                    ErrorCode: LoginErrorCode.TierNotEntitled);
            }

            // Issue JWT scoped to target org
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _identityRepository.UpdateUserAsync(user, ct);

            var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, organization, platformUser.Id, tierResolution.Tier, ct);

            _logger.LogInformation("Subdomain login succeeded - {Email} in org {Subdomain} (UserId: {UserId}, tier: {Tier})",
                email, orgSubdomain, user.Id, tierResolution.Tier);

            // Welcome email — once per user across all login paths.
            await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);

            return new LoginResult(true, Tokens: tokenResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subdomain login failed with exception - {Email} in {Subdomain}", email, orgSubdomain);
            return new LoginResult(false, Error: "Invalid email or password.");
        }
    }
}
