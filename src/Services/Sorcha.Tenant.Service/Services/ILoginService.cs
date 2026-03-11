// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Error codes for login failures — avoids fragile string-matching on error messages.
/// </summary>
public enum LoginErrorCode
{
    /// <summary>No error.</summary>
    None,
    /// <summary>Invalid email or password.</summary>
    InvalidCredentials,
    /// <summary>Too many failed attempts — rate limited.</summary>
    RateLimited,
    /// <summary>Account is locked (temporary or permanent).</summary>
    AccountLocked
}

/// <summary>
/// Result of a login attempt.
/// </summary>
/// <param name="Success">Whether login succeeded (tokens issued or 2FA challenge returned).</param>
/// <param name="Tokens">Access and refresh tokens if login completed without 2FA.</param>
/// <param name="TwoFactorRequired">True if 2FA verification is needed before tokens can be issued.</param>
/// <param name="LoginToken">Short-lived token for 2FA verification flow.</param>
/// <param name="AvailableMethods">Available 2FA methods (e.g., "totp", "passkey").</param>
/// <param name="Error">Error message if login failed.</param>
/// <param name="ErrorCode">Typed error code for programmatic handling.</param>
public record LoginResult(
    bool Success,
    TokenResponse? Tokens = null,
    bool TwoFactorRequired = false,
    string? LoginToken = null,
    List<string>? AvailableMethods = null,
    string? Error = null,
    LoginErrorCode ErrorCode = LoginErrorCode.None);

/// <summary>
/// Authenticates users with email and password. Handles BCrypt verification,
/// 2FA detection, and JWT token issuance. Used by both the API endpoint
/// and the server-rendered Login Razor Page.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Attempts to authenticate a user with email and password.
    /// Returns tokens directly if no 2FA is configured, or a login token
    /// for 2FA verification if TOTP/passkey is enabled.
    /// </summary>
    /// <param name="email">User email address.</param>
    /// <param name="password">User password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login result with tokens or 2FA challenge.</returns>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
}
