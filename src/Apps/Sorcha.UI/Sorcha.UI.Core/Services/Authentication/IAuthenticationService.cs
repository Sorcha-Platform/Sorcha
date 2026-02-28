// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Service for handling OAuth2 Password Grant authentication
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a user with username and password
    /// </summary>
    /// <param name="request">Login request with credentials</param>
    /// <param name="profileName">Profile name to use for authentication</param>
    /// <returns>Token response with access and refresh tokens</returns>
    Task<TokenResponse> LoginAsync(LoginRequest request, string profileName);

    /// <summary>
    /// Gets the cached access token for the specified profile
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>Access token or null if not found/expired</returns>
    Task<string?> GetAccessTokenAsync(string profileName);

    /// <summary>
    /// Gets the cached refresh token for the specified profile
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>Refresh token or null if not found</returns>
    Task<string?> GetRefreshTokenAsync(string profileName);

    /// <summary>
    /// Refreshes the access token using the refresh token
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>True if refresh succeeded, false otherwise</returns>
    Task<bool> RefreshTokenAsync(string profileName);

    /// <summary>
    /// Logs out the user and clears cached tokens
    /// </summary>
    /// <param name="profileName">Profile name</param>
    Task LogoutAsync(string profileName);

    /// <summary>
    /// Checks if the user is authenticated for the specified profile
    /// </summary>
    /// <param name="profileName">Profile name</param>
    /// <returns>True if authenticated with valid token, false otherwise</returns>
    Task<bool> IsAuthenticatedAsync(string profileName);

    /// <summary>
    /// Gets detailed authentication state information
    /// </summary>
    /// <returns>Authentication state with user info and roles</returns>
    Task<AuthenticationStateInfo> GetAuthenticationInfoAsync();
}
