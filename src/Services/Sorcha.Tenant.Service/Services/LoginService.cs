// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

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
    private readonly ILogger<LoginService> _logger;

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
        ILogger<LoginService> logger)
    {
        _dbContext = dbContext;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _tokenService = tokenService;
        _totpService = totpService;
        _passkeyService = passkeyService;
        _revocationService = revocationService;
        _platformUserService = platformUserService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
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
            // Look up user by email
            var user = await _identityRepository.GetUserByEmailAsync(email, ct);

            if (user is null || user.Status != IdentityStatus.Active)
            {
                _logger.LogWarning("Login failed: User not found or inactive - {Email}", email);
                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Look up PlatformUser for authentication fields
            var platformUser = await _dbContext.PlatformUsers
                .FirstOrDefaultAsync(p => p.Id == user.PlatformUserId, ct);
            if (platformUser is null)
            {
                _logger.LogError("Login failed: PlatformUser not found for user {UserId}", user.Id);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Validate password with progressive lockout on PlatformUser
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

            // Get user's organization
            var organization = await _organizationRepository.GetByIdAsync(user.OrganizationId, ct);

            if (organization is null)
            {
                _logger.LogError("Login failed: Organization not found - {OrgId}", user.OrganizationId);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // Reset failed attempts on successful password verification
            await _revocationService.ResetFailedAuthAttemptsAsync(email, ct);

            // Check if user has TOTP 2FA or passkeys enabled
            var totpStatus = await _totpService.GetStatusAsync(user.Id, ct);
            var passkeys = await _passkeyService.GetCredentialsByOwnerAsync(user.PlatformUserId, ct);
            var hasActivePasskeys = passkeys.Any(p => p.Status == CredentialStatus.Active);

            if (totpStatus.IsEnabled || hasActivePasskeys)
            {
                // 2FA required: issue a short-lived login token instead of JWT
                var loginToken = await _totpService.GenerateLoginTokenAsync(user.Id, ct);

                var methods = new List<string>();
                if (totpStatus.IsEnabled) methods.Add("totp");
                if (hasActivePasskeys) methods.Add("passkey");

                _logger.LogInformation("Login requires 2FA for user {Email} (UserId: {UserId}), methods: {Methods}",
                    user.Email, user.Id, string.Join(", ", methods));

                return new LoginResult(true, TwoFactorRequired: true,
                    LoginToken: loginToken, AvailableMethods: methods);
            }

            // No 2FA — standard login: update timestamp and issue JWT
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _identityRepository.UpdateUserAsync(user, ct);

            // Generate tokens
            var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, organization, user.PlatformUserId, ct);

            _logger.LogInformation("User logged in successfully - {Email} (UserId: {UserId}, OrgId: {OrgId})",
                user.Email, user.Id, organization.Id);

            return new LoginResult(true, Tokens: tokenResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed with exception - {Email}", email);
            return new LoginResult(false, Error: "Invalid email or password.");
        }
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string email, string password, string orgSubdomain, CancellationToken ct = default)
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

            // Issue JWT scoped to target org
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _identityRepository.UpdateUserAsync(user, ct);

            var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, organization, platformUser.Id, ct);

            _logger.LogInformation("Subdomain login succeeded - {Email} in org {Subdomain} (UserId: {UserId})",
                email, orgSubdomain, user.Id);

            return new LoginResult(true, Tokens: tokenResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subdomain login failed with exception - {Email} in {Subdomain}", email, orgSubdomain);
            return new LoginResult(false, Error: "Invalid email or password.");
        }
    }
}
