// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Json;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Service to sync authentication state from server cookies to WASM LocalStorage.
/// Called on WASM app initialization to pick up tokens from the server-side login.
/// </summary>
public class AuthStateSync
{
    private readonly HttpClient _httpClient;
    private readonly ITokenCache _tokenCache;
    private readonly IConfigurationService _configurationService;

    public AuthStateSync(
        HttpClient httpClient,
        ITokenCache tokenCache,
        IConfigurationService configurationService)
    {
        _httpClient = httpClient;
        _tokenCache = tokenCache;
        _configurationService = configurationService;
    }

    /// <summary>
    /// Syncs auth state from server cookie to LocalStorage.
    /// Called on app initialization.
    /// </summary>
    public async Task SyncAuthStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/ui-auth/state");
            
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var authState = await response.Content.ReadFromJsonAsync<AuthStateResponse>();
            
            if (authState == null || !authState.IsAuthenticated)
            {
                return;
            }

            var profileName = authState.ProfileName ?? "Development";
            var existingToken = await _tokenCache.GetTokenAsync(profileName);
            
            if (existingToken != null && !existingToken.IsExpired)
            {
                return;
            }

            if (!string.IsNullOrEmpty(authState.AccessToken))
            {
                var expiresAt = DateTimeOffset.TryParse(authState.ExpiresAt, out var exp) 
                    ? exp.UtcDateTime 
                    : DateTime.UtcNow.AddHours(1);

                var cacheEntry = new TokenCacheEntry
                {
                    AccessToken = authState.AccessToken,
                    RefreshToken = authState.RefreshToken ?? "",
                    ExpiresAt = expiresAt,
                    ProfileName = profileName,
                    IssuedAt = DateTime.UtcNow
                };

                await _tokenCache.StoreTokenAsync(profileName, cacheEntry);
                await _configurationService.SetActiveProfileAsync(profileName);
            }
        }
        catch
        {
            // Silently fail - user will need to log in again
        }
    }
}

public class AuthStateResponse
{
    public bool IsAuthenticated { get; set; }
    public string? Username { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? ExpiresAt { get; set; }
    public string? ProfileName { get; set; }
}
