// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Authentication service for token management.
/// Login and registration are handled by server-rendered Razor Pages;
/// this service manages token refresh, retrieval, and logout.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenCache _tokenCache;
    private readonly IConfigurationService _configurationService;
    private readonly JwtSecurityTokenHandler _jwtHandler = new();

    public AuthenticationService(
        HttpClient httpClient,
        ITokenCache tokenCache,
        IConfigurationService configurationService)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _configurationService = configurationService;
    }

    /// <inheritdoc />
    public async Task<string?> GetAccessTokenAsync(string profileName)
    {
        var entry = await _tokenCache.GetTokenAsync(profileName);

        if (entry == null)
            return null;

        // Attempt refresh if expired or near expiration
        if ((entry.IsExpired || entry.IsNearExpiration) && !string.IsNullOrEmpty(entry.RefreshToken))
        {
            var refreshed = await RefreshTokenAsync(profileName);
            if (refreshed)
            {
                entry = await _tokenCache.GetTokenAsync(profileName);
                return entry?.AccessToken;
            }

            // Refresh failed — if token was expired, return null; if near-expiry, return stale token
            return entry.IsExpired ? null : entry.AccessToken;
        }

        return entry.AccessToken;
    }

    /// <inheritdoc />
    public async Task<string?> GetRefreshTokenAsync(string profileName)
    {
        var entry = await _tokenCache.GetTokenAsync(profileName);
        return entry?.RefreshToken;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshTokenAsync(string profileName)
    {
        var refreshToken = await GetRefreshTokenAsync(profileName);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return false;
        }

        var profile = await _configurationService.GetProfileAsync(profileName);
        if (profile == null)
        {
            return false;
        }

        try
        {
            var formData = new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["client_id"] = "sorcha-ui-web"
            };

            // Get the auth token URL from profile (uses override or derives from base URL)
            var tokenUrl = profile.GetAuthTokenUrl();
            if (string.IsNullOrEmpty(tokenUrl))
            {
                // Fallback to relative path for same-origin requests
                tokenUrl = "/api/service-auth/token";
            }

            var response = await _httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(formData));

            if (!response.IsSuccessStatusCode)
            {
                await _tokenCache.RemoveTokenAsync(profileName);
                return false;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokenResponse == null || !tokenResponse.IsValid())
            {
                return false;
            }

            var cacheEntry = TokenCacheEntry.FromTokenResponse(tokenResponse, profileName);
            await _tokenCache.StoreTokenAsync(profileName, cacheEntry);

            return true;
        }
        catch (Exception)
        {
            // Resilience: token refresh failure clears stale tokens and returns false
            // so callers can prompt re-authentication
            await _tokenCache.RemoveTokenAsync(profileName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync(string profileName)
    {
        // Clear local token cache — server-side revocation is handled
        // by the server-rendered logout page which receives the refresh token
        await _tokenCache.RemoveTokenAsync(profileName);
    }

    /// <inheritdoc />
    public async Task<bool> IsAuthenticatedAsync(string profileName)
    {
        var entry = await _tokenCache.GetTokenAsync(profileName);
        return entry != null && !entry.IsExpired;
    }

    /// <inheritdoc />
    public async Task<AuthenticationStateInfo> GetAuthenticationInfoAsync()
    {
        var activeProfileName = await _configurationService.GetActiveProfileNameAsync();
        var entry = await _tokenCache.GetTokenAsync(activeProfileName);

        if (entry == null || entry.IsExpired)
        {
            return AuthenticationStateInfo.Unauthenticated();
        }

        try
        {
            var jwtToken = _jwtHandler.ReadJwtToken(entry.AccessToken);
            var username = jwtToken.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                           ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;

            var roles = jwtToken.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .ToList();

            return new AuthenticationStateInfo
            {
                IsAuthenticated = true,
                Username = username,
                Roles = roles,
                ProfileName = activeProfileName,
                ExpiresAt = entry.ExpiresAt
            };
        }
        catch (Exception)
        {
            // Resilience: malformed JWT returns unauthenticated rather than crashing the UI
            return AuthenticationStateInfo.Unauthenticated();
        }
    }
}
