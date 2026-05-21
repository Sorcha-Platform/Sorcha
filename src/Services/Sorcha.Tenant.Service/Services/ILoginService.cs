// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;
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
    bool OrgSelectionRequired = false,
    List<OrgChoice>? AvailableOrganizations = null,
    string? PlatformLoginToken = null,
    string? Error = null,
    LoginErrorCode ErrorCode = LoginErrorCode.None);

/// <summary>
/// An organisation the user can choose to log into.
/// </summary>
/// <param name="OrganizationId">Organisation ID.</param>
/// <param name="OrganizationName">Display name.</param>
/// <param name="Subdomain">URL subdomain.</param>
/// <param name="Role">User's role in this org.</param>
public record OrgChoice(
    Guid OrganizationId,
    string OrganizationName,
    string Subdomain,
    string Role);

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
    /// <param name="requestedTier">The trust tier to mint (spec 136). Default Platform; pass Consumer for a consumer destination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login result with tokens or 2FA challenge.</returns>
    Task<LoginResult> LoginAsync(string email, string password, Tier requestedTier = Tier.Platform, CancellationToken ct = default);

    /// <summary>
    /// Attempts to authenticate a user with email and password, targeting a specific organization by subdomain.
    /// Validates PlatformUserOrgMembership for the target org and issues JWT scoped to that org.
    /// Returns 403-equivalent if the user is not a member of the specified org.
    /// </summary>
    /// <param name="email">User email address.</param>
    /// <param name="password">User password.</param>
    /// <param name="orgSubdomain">Target organization subdomain.</param>
    /// <param name="requestedTier">The trust tier to mint (spec 136). Default Platform.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login result with tokens scoped to the target org, or 2FA challenge.</returns>
    Task<LoginResult> LoginAsync(string email, string password, string orgSubdomain, Tier requestedTier = Tier.Platform, CancellationToken ct = default);

    /// <summary>
    /// Completes login after org selection. Uses the platform login token issued during
    /// the initial login to identify the user, then issues a JWT scoped to the chosen org.
    /// </summary>
    /// <param name="platformLoginToken">Short-lived token from the org-selection login response.</param>
    /// <param name="organizationId">The chosen organisation ID.</param>
    /// <param name="requestedTier">The trust tier to mint (spec 136). Default Platform — org selection is a platform action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Login result with tokens scoped to the chosen org, or 2FA challenge.</returns>
    Task<LoginResult> CompleteOrgSelectionAsync(string platformLoginToken, Guid organizationId, Tier requestedTier = Tier.Platform, CancellationToken ct = default);
}
