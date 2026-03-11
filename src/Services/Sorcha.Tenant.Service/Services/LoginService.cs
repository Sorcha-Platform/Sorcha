// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Authenticates users with email/password, checking BCrypt hash,
/// 2FA requirements, and issuing JWT tokens.
/// </summary>
public class LoginService : ILoginService
{
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly IPasskeyService _passkeyService;
    private readonly ITokenRevocationService _revocationService;
    private readonly ILogger<LoginService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginService"/> class.
    /// </summary>
    public LoginService(
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        ITotpService totpService,
        IPasskeyService passkeyService,
        ITokenRevocationService revocationService,
        ILogger<LoginService> logger)
    {
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _tokenService = tokenService;
        _totpService = totpService;
        _passkeyService = passkeyService;
        _revocationService = revocationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // Rate limiting check
        if (await _revocationService.IsRateLimitedAsync(email, ct))
        {
            _logger.LogWarning("Login rate-limited for {Email}", email);
            return new LoginResult(false, Error: "Too many login attempts. Please try again later.");
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

            // Password hash check (null means external IDP user)
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogWarning("Login failed: User has no password (external IDP user?) - {Email}", email);
                await _revocationService.IncrementFailedAuthAttemptsAsync(email, ct);
                return new LoginResult(false, Error: "Invalid email or password.");
            }

            // BCrypt verification
            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed: Invalid password - {Email}", email);
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
            var passkeys = await _passkeyService.GetCredentialsByOwnerAsync(OwnerTypes.OrgUser, user.Id, ct);
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
            var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, organization, ct);

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
}
