// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages email verification tokens and verification state.
/// Tokens are 32-byte URL-safe base64 strings with a 24-hour expiry.
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Generates a verification token for a user and optionally sends the verification email.
    /// </summary>
    Task<string> GenerateAndSendVerificationAsync(
        UserIdentity user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a verification token and marks the user's email as verified.
    /// Returns true if the token was valid and the email was verified.
    /// </summary>
    Task<(bool Success, string? Error)> VerifyTokenAsync(
        string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a resend is allowed (rate limit: 3 per hour per user).
    /// </summary>
    Task<bool> CanResendAsync(Guid userId, CancellationToken cancellationToken = default);
}
