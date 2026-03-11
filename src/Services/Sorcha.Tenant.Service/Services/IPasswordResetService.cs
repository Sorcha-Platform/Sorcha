// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of validating a password reset token.
/// </summary>
/// <param name="IsValid">Whether the token is valid and not expired.</param>
/// <param name="Email">The email address of the user (if valid).</param>
/// <param name="Error">Error message (if invalid).</param>
public record PasswordResetValidation(bool IsValid, string? Email = null, string? Error = null);

/// <summary>
/// Result of a password reset attempt.
/// </summary>
/// <param name="Success">Whether the password was successfully reset.</param>
/// <param name="Error">General error message (if failed).</param>
/// <param name="ValidationErrors">Field-level validation errors from password policy (if any).</param>
public record PasswordResetResult(
    bool Success, string? Error = null,
    Dictionary<string, string[]>? ValidationErrors = null);

/// <summary>
/// Manages password reset token generation, validation, and password updates.
/// Tokens are 32-byte URL-safe base64 strings with a 1-hour TTL.
/// The raw token is sent to the user via email; only the SHA-256 hash is stored.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Generates a reset token, stores the hash on the user, and sends
    /// a reset email. Returns true even if the email is not found
    /// (to prevent user enumeration).
    /// </summary>
    /// <param name="email">User's email address.</param>
    /// <param name="resetBaseUrl">Base URL for constructing the reset link (e.g., "https://app.sorcha.io/auth/reset-password").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Always true to prevent user enumeration.</returns>
    Task<bool> RequestResetAsync(string email, string resetBaseUrl, CancellationToken ct = default);

    /// <summary>
    /// Validates that a reset token is valid and not expired.
    /// </summary>
    /// <param name="token">The raw reset token from the email link.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with the user's email if valid.</returns>
    Task<PasswordResetValidation> ValidateTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Resets the user's password after validating the token and the new
    /// password against NIST policy + HIBP breach list.
    /// </summary>
    /// <param name="token">The raw reset token from the email link.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or validation errors.</returns>
    Task<PasswordResetResult> ResetPasswordAsync(
        string token, string newPassword, CancellationToken ct = default);
}
