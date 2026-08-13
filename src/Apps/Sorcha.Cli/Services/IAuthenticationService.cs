// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Cli.Models;
using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Cli.Services;

/// <summary>
/// Service for handling authentication and token management.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a user with email and password via <c>POST /api/auth/login</c> (issue #1402).
    /// Caches the token for subsequent requests.
    /// </summary>
    /// <param name="request">Login request</param>
    /// <param name="profileName">Profile to use for authentication</param>
    /// <param name="organizationId">
    /// Optional organisation to pre-select when the account belongs to more than one organisation.
    /// When the account is multi-org and this is <see langword="null"/>, the call throws
    /// <see cref="OrgSelectionRequiredException"/> instead of completing login.
    /// </param>
    /// <returns>Token response</returns>
    Task<TokenResponse> LoginAsync(UserLoginRequest request, string profileName, Guid? organizationId = null);

    /// <summary>
    /// Completes login for a multi-org user after <see cref="OrgSelectionRequiredException"/> was
    /// thrown by <see cref="LoginAsync"/>, via <c>POST /api/auth/select-org</c>. Caches the
    /// resulting token for subsequent requests.
    /// </summary>
    /// <param name="platformLoginToken">The short-lived token from the org-selection response.</param>
    /// <param name="organizationId">The chosen organisation.</param>
    /// <param name="profileName">Profile to use for authentication.</param>
    /// <param name="subjectEmail">The user's email, for the cached token's subject.</param>
    /// <returns>Token response</returns>
    Task<TokenResponse> CompleteOrgSelectionAsync(string platformLoginToken, Guid organizationId, string profileName, string subjectEmail);

    /// <summary>
    /// Authenticates a service principal with client credentials.
    /// Caches the token for subsequent requests.
    /// </summary>
    /// <param name="request">Service principal login request</param>
    /// <param name="profileName">Profile to use for authentication</param>
    /// <returns>Token response</returns>
    Task<TokenResponse> LoginServicePrincipalAsync(ServicePrincipalLoginRequest request, string profileName);

    /// <summary>
    /// Gets a valid access token for the specified profile.
    /// Returns cached token if valid, refreshes if expiring soon, or returns null if not authenticated.
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>Access token, or null if not authenticated</returns>
    Task<string?> GetAccessTokenAsync(string profileName);

    /// <summary>
    /// Refreshes an expired or expiring token using the refresh token.
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>New token response, or null if refresh failed</returns>
    Task<TokenResponse?> RefreshTokenAsync(string profileName);

    /// <summary>
    /// Checks if the user is authenticated for the specified profile.
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>True if authenticated with a valid token, false otherwise</returns>
    Task<bool> IsAuthenticatedAsync(string profileName);

    /// <summary>
    /// Logs out by clearing the cached token for the specified profile.
    /// </summary>
    /// <param name="profileName">Profile name</param>
    Task LogoutAsync(string profileName);

    /// <summary>
    /// Logs out from all profiles by clearing all cached tokens.
    /// </summary>
    Task LogoutAllAsync();
}
