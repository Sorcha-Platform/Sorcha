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

        // Store on user entity
        user.VerificationToken = token;
        user.VerificationTokenExpiresAt = DateTimeOffset.UtcNow.Add(TokenExpiry);

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
        var user = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.VerificationToken == token, cancellationToken);

        if (user is null)
        {
            return (false, "Invalid verification token.");
        }

        if (user.VerificationTokenExpiresAt.HasValue
            && user.VerificationTokenExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return (false, "Verification token has expired.");
        }

        // Mark email as verified
        user.EmailVerified = true;
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.VerificationToken = null;
        user.VerificationTokenExpiresAt = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Email verified for user {UserId} ({Email})",
            user.Id, user.Email);

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<bool> CanResendAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return false;

        // Simple rate check: if token was generated within the last 20 minutes, deny
        // (3 per hour ≈ one every 20 minutes)
        if (user.VerificationTokenExpiresAt.HasValue)
        {
            var tokenAge = DateTimeOffset.UtcNow - (user.VerificationTokenExpiresAt.Value - TokenExpiry);
            if (tokenAge < TimeSpan.FromMinutes(20))
                return false;
        }

        return true;
    }
}
