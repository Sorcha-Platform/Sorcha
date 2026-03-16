// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
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
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailVerificationService> _logger;

    private static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(24);

    public EmailVerificationService(
        TenantDbContext dbContext,
        IEmailSender emailSender,
        ILogger<EmailVerificationService> logger)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
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

        // Send verification email
        await _emailSender.SendAsync(
            user.Email,
            "Verify your email address",
            $"Please verify your email by using this token: {token}",
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
