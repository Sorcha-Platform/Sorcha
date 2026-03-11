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
    private readonly IEmailSender _emailSender;
    private readonly ILogger<PasswordResetService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordResetService"/> class.
    /// </summary>
    public PasswordResetService(
        TenantDbContext dbContext,
        IPasswordPolicyService passwordPolicyService,
        IEmailSender emailSender,
        ILogger<PasswordResetService> logger)
    {
        _dbContext = dbContext;
        _passwordPolicyService = passwordPolicyService;
        _emailSender = emailSender;
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

        // External IDP users cannot reset passwords (they have no local password)
        if (string.IsNullOrEmpty(user.PasswordHash))
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

        // Store the SHA-256 hash of the token (not the raw token)
        user.PasswordResetTokenHash = HashToken(rawToken);
        user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenTtl);

        await _dbContext.SaveChangesAsync(ct);

        // Send the reset email with the raw token
        var resetLink = $"{resetBaseUrl.TrimEnd('/')}?token={Uri.EscapeDataString(rawToken)}";
        var htmlBody = BuildResetEmailHtml(user.DisplayName, resetLink);
        await _emailSender.SendAsync(user.Email, "Reset your password", htmlBody, ct);

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
        var user = await FindUserByTokenHashAsync(tokenHash, ct);

        if (user is null)
        {
            return new PasswordResetValidation(false, Error: "Invalid or expired reset token.");
        }

        if (user.PasswordResetTokenExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogInformation("Expired password reset token used for user {Email}", user.Email);
            return new PasswordResetValidation(false, Error: "Reset token has expired. Please request a new one.");
        }

        return new PasswordResetValidation(true, Email: user.Email);
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
        var user = await FindUserByTokenHashAsync(tokenHash, ct);

        if (user is null)
        {
            return new PasswordResetResult(false, Error: "Invalid or expired reset token.");
        }

        if (user.PasswordResetTokenExpiresAt < DateTimeOffset.UtcNow)
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
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Password successfully reset for user {Email} (UserId: {UserId})",
            user.Email, user.Id);

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
    /// Finds a user by the hashed reset token.
    /// </summary>
    private async Task<UserIdentity?> FindUserByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        return await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, ct);
    }

    /// <summary>
    /// Builds the HTML body for the password reset email.
    /// </summary>
    private static string BuildResetEmailHtml(string displayName, string resetLink)
    {
        return $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto;">
                <h2>Reset Your Password</h2>
                <p>Hi {System.Net.WebUtility.HtmlEncode(displayName)},</p>
                <p>We received a request to reset your password. Click the link below to set a new password:</p>
                <p style="margin: 24px 0;">
                    <a href="{System.Net.WebUtility.HtmlEncode(resetLink)}"
                       style="background-color: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px;">
                        Reset Password
                    </a>
                </p>
                <p>This link will expire in 1 hour. If you didn't request a password reset, you can safely ignore this email.</p>
                <p style="color: #6b7280; font-size: 14px; margin-top: 32px;">
                    If the button doesn't work, copy and paste this link into your browser:<br/>
                    <a href="{System.Net.WebUtility.HtmlEncode(resetLink)}">{System.Net.WebUtility.HtmlEncode(resetLink)}</a>
                </p>
            </div>
            """;
    }
}
