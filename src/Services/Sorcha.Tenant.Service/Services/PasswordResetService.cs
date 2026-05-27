// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages password reset token generation, validation, and password updates.
/// Tokens are 32-byte cryptographically random values encoded as URL-safe base64.
/// Only the SHA-256 hash of the token is stored in the database to prevent
/// token compromise if the database is leaked.
/// </summary>
public class PasswordResetService : IPasswordResetService
{
    /// <summary>
    /// Token time-to-live: 1 hour from generation.
    /// </summary>
    private static readonly TimeSpan TokenTtl = TimeSpan.FromHours(1);

    private readonly TenantDbContext _dbContext;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly ITransactionalEmailService _transactional;
    private readonly ITenantSecurityInboxWriter _securityInbox;
    private readonly ILogger<PasswordResetService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetService"/> class.
    /// </summary>
    public PasswordResetService(
        TenantDbContext dbContext,
        IPasswordPolicyService passwordPolicyService,
        ITransactionalEmailService transactional,
        ITenantSecurityInboxWriter securityInbox,
        ILogger<PasswordResetService> logger)
    {
        _dbContext = dbContext;
        _passwordPolicyService = passwordPolicyService;
        _transactional = transactional;
        _securityInbox = securityInbox;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> RequestResetAsync(string email, string resetBaseUrl, CancellationToken ct = default)
    {
        var user = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Email == email && u.Status == IdentityStatus.Active, ct);

        if (user is null)
        {
            // Return true to prevent user enumeration — don't reveal whether the email exists
            _logger.LogInformation("Password reset requested for non-existent or inactive email: {Email}", email);
            return true;
        }

        // Look up PlatformUser for authentication fields
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == user.PlatformUserId, ct);

        // External IDP users cannot reset passwords (they have no local password)
        if (platformUser is null || string.IsNullOrEmpty(platformUser.PasswordHash))
        {
            _logger.LogInformation("Password reset requested for external IDP user: {Email}", email);
            return true;
        }

        // Generate a cryptographically secure 32-byte token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        // Store the SHA-256 hash of the token (not the raw token) on PlatformUser
        platformUser.PasswordResetTokenHash = HashToken(rawToken);
        platformUser.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenTtl);

        await _dbContext.SaveChangesAsync(ct);

        // Send the reset email via the templated facade so it shares the Sorcha
        // base layout with verification, welcome, and invitation emails.
        var resetLink = $"{resetBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(rawToken)}";
        await _transactional.SendPasswordResetAsync(
            new ResetPasswordDispatch(
                ToEmail: user.Email,
                DisplayName: user.DisplayName,
                ResetUrl: resetLink,
                ExpiresInMinutes: (int)TokenTtl.TotalMinutes),
            ct);

        _logger.LogInformation("Password reset token generated and email sent for user {Email} (UserId: {UserId})",
            user.Email, user.Id);

        return true;
    }

    /// <inheritdoc />
    public async Task<PasswordResetValidation> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetValidation(false, Error: "Reset token is required.");
        }

        var tokenHash = HashToken(token);
        var platformUser = await FindPlatformUserByTokenHashAsync(tokenHash, ct);

        if (platformUser is null)
        {
            return new PasswordResetValidation(false, Error: "Invalid or expired reset token.");
        }

        if (platformUser.PasswordResetTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogInformation("Expired password reset token used for user {Email}", platformUser.Email);
            return new PasswordResetValidation(false, Error: "Reset token has expired. Please request a new one.");
        }

        return new PasswordResetValidation(true, Email: platformUser.Email);
    }

    /// <inheritdoc />
    public async Task<PasswordResetResult> ResetPasswordAsync(
        string token, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetResult(false, Error: "Reset token is required.");
        }

        var tokenHash = HashToken(token);
        var platformUser = await FindPlatformUserByTokenHashAsync(tokenHash, ct);

        if (platformUser is null)
        {
            return new PasswordResetResult(false, Error: "Invalid or expired reset token.");
        }

        if (platformUser.PasswordResetTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            return new PasswordResetResult(false, Error: "Reset token has expired. Please request a new one.");
        }

        // Validate the new password against NIST policy + HIBP breach check
        var passwordResult = await _passwordPolicyService.ValidateAsync(newPassword, ct);
        if (!passwordResult.IsValid)
        {
            return new PasswordResetResult(false,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    ["password"] = passwordResult.Errors.ToArray()
                });
        }

        // Update the password hash (BCrypt) and clear the reset token (one-time use)
        platformUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        platformUser.PasswordResetTokenHash = null;
        platformUser.PasswordResetTokenExpiresAt = null;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Password successfully reset for PlatformUser {Email} (PlatformUserId: {PlatformUserId})",
            platformUser.Email, platformUser.Id);

        // Feature 118 — emit a Category=Security inbox entry. Fail-safe.
        await _securityInbox.WritePasswordResetAsync(platformUser.Id, ct);

        return new PasswordResetResult(true);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a raw token, returned as a hex string.
    /// </summary>
    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Finds a PlatformUser by the hashed reset token.
    /// </summary>
    private async Task<PlatformUser?> FindPlatformUserByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.PasswordResetTokenHash == tokenHash, ct);
    }

}
