// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Service for authentication token management.
/// Login and registration are handled by server-rendered Razor Pages;
/// this interface covers token refresh, retrieval, and logout.
/// </summary>
public interface IAuthenticationService
{
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
    /// Attempts a one-shot trust-tier upgrade (spec 136 defense-in-depth). Re-mints the cached
    /// token at the requested tier via the JSON refresh endpoint, which the server gates on
    /// entitlement — an entitled holder is upgraded, a non-entitled one is left on their current
    /// tier (the call simply returns false / a same-tier token). Used by the <c>/app</c> platform
    /// host to self-heal a stale consumer-tier token for an entitled admin. Never throws.
    /// </summary>
    /// <param name="profileName">Profile name.</param>
    /// <param name="tier">Requested human tier hint (e.g. <c>"platform"</c>).</param>
    /// <returns>True if a new token was minted and cached, false otherwise.</returns>
    Task<bool> TryUpgradeTierAsync(string profileName, string tier);

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
