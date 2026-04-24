// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages email verification with 32-byte URL-safe base64 tokens and 24-hour expiry.
/// Rate limits resend requests to 3 per hour per user.
/// </summary>
public class EmailVerificationService : IEmailVerificationService
{
    private readonly TenantDbContext _dbContext;
    private readonly ITransactionalEmailService _transactional;
    private readonly WelcomeEmailDispatcher _welcomeDispatcher;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<EmailVerificationService> _logger;

    private static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(24);

    public EmailVerificationService(
        TenantDbContext dbContext,
        ITransactionalEmailService transactional,
        WelcomeEmailDispatcher welcomeDispatcher,
        IOptions<EmailSettings> emailSettings,
        ILogger<EmailVerificationService> logger)
    {
        _dbContext = dbContext;
        _transactional = transactional;
        _welcomeDispatcher = welcomeDispatcher;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GenerateAndSendVerificationAsync(
        UserIdentity user, CancellationToken cancellationToken)
    {
        // Generate 32-byte URL-safe base64 token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        // Store verification token on PlatformUser
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == user.PlatformUserId, cancellationToken);
        platformUser!.VerificationToken = token;
        platformUser.VerificationTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenExpiry);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Send the verification email via the templated facade. The verify URL takes
        // the user to the Tenant Service Razor Page at /auth/verify-email.
        var verifyUrl = $"{_emailSettings.BaseUrl.TrimEnd('/')}/auth/verify-email?token={Uri.EscapeDataString(token)}";
        await _transactional.SendVerificationAsync(
            new VerifyEmailDispatch(
                ToEmail: user.Email,
                DisplayName: user.DisplayName,
                VerifyUrl: verifyUrl,
                ExpiresInHours: (int)TokenExpiry.TotalHours),
            cancellationToken);

        _logger.LogInformation(
            "Email verification sent to {Email} for user {UserId}",
            user.Email, user.Id);

        return token;
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? Error)> VerifyTokenAsync(
        string token, CancellationToken cancellationToken)
    {
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.VerificationToken == token, cancellationToken);

        if (platformUser is null)
        {
            return (false, "Invalid verification token.");
        }

        if (platformUser.VerificationTokenExpiresAt.HasValue
            && platformUser.VerificationTokenExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return (false, "Verification token has expired.");
        }

        // Mark email as verified on PlatformUser
        platformUser.EmailVerified = true;
        platformUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
        platformUser.VerificationToken = null;
        platformUser.VerificationTokenExpiresAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Email verified for PlatformUser {PlatformUserId} ({Email})",
            platformUser.Id, platformUser.Email);

        // Fire the welcome email if the user hasn't had one yet. The dispatcher is
        // idempotent and non-throwing — a send failure will not reverse verification.
        await _welcomeDispatcher.SendIfPendingAsync(platformUser, cancellationToken);

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<bool> CanResendAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Look up the UserIdentity to get the PlatformUserId
        var user = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return false;

        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == user.PlatformUserId, cancellationToken);

        if (platformUser is null)
            return false;

        // Simple rate check: if token was generated within the last 20 minutes, deny
        // (3 per hour ≈ one every 20 minutes)
        if (platformUser.VerificationTokenExpiresAt.HasValue)
        {
            var tokenAge = DateTimeOffset.UtcNow - (platformUser.VerificationTokenExpiresAt.Value - TokenExpiry);
            if (tokenAge < TimeSpan.FromMinutes(20))
                return false;
        }

        return true;
    }
}
