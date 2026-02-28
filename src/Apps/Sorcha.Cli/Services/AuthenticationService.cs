// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Json;
using System.Text.Json;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Service for handling authentication and token management.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IConfigurationService _configService;
    private readonly TokenCache _tokenCache;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuthenticationService(
        IConfigurationService configService,
        TokenCache tokenCache,
        HttpClient httpClient)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc/>
    public async Task<TokenResponse> LoginAsync(LoginRequest request, string profileName)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new ArgumentException("Username cannot be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(request));
        }

        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        // Prepare OAuth2 password grant request
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = request.Username,
            ["password"] = request.Password,
            ["client_id"] = request.ClientId ?? profile.DefaultClientId ?? "sorcha-cli"
        };

        if (!string.IsNullOrEmpty(request.Scope))
        {
            formData["scope"] = request.Scope;
        }

        var tokenResponse = await PostTokenRequestAsync(profile.GetAuthTokenUrl(), formData);

        // Cache the token
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            Profile = profileName,
            Subject = request.Username
        };

        await _tokenCache.SetAsync(profileName, cacheEntry);

        return tokenResponse;
    }

    /// <inheritdoc/>
    public async Task<TokenResponse> LoginServicePrincipalAsync(ServicePrincipalLoginRequest request, string profileName)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new ArgumentException("Client ID cannot be empty.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            throw new ArgumentException("Client secret cannot be empty.", nameof(request));
        }

        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        // Prepare OAuth2 client credentials grant request
        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = request.ClientId,
            ["client_secret"] = request.ClientSecret
        };

        if (!string.IsNullOrEmpty(request.Scope))
        {
            formData["scope"] = request.Scope;
        }

        var tokenResponse = await PostTokenRequestAsync(profile.GetAuthTokenUrl(), formData);

        // Cache the token
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            Profile = profileName,
            Subject = request.ClientId
        };

        await _tokenCache.SetAsync(profileName, cacheEntry);

        return tokenResponse;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(string profileName)
    {
        var cachedToken = await _tokenCache.GetAsync(profileName);

        if (cachedToken == null)
        {
            return null; // Not authenticated
        }

        // If token is expiring soon (within 5 minutes), try to refresh it
        if (cachedToken.IsExpiringSoon(5))
        {
            var refreshedToken = await RefreshTokenAsync(profileName);
            if (refreshedToken != null)
            {
                return refreshedToken.AccessToken;
            }

            // Refresh failed, return null (user needs to re-authenticate)
            return null;
        }

        return cachedToken.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<TokenResponse?> RefreshTokenAsync(string profileName)
    {
        var cachedToken = await _tokenCache.GetAsync(profileName);

        if (cachedToken == null || string.IsNullOrEmpty(cachedToken.RefreshToken))
        {
            return null; // No refresh token available
        }

        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            return null;
        }

        try
        {
            // Prepare OAuth2 refresh token grant request
            var formData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = cachedToken.RefreshToken,
                ["client_id"] = profile.DefaultClientId ?? "sorcha-cli"
            };

            var tokenResponse = await PostTokenRequestAsync(profile.GetAuthTokenUrl(), formData);

            // Update cache with new token
            var cacheEntry = new TokenCacheEntry
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken ?? cachedToken.RefreshToken, // Keep old refresh token if not provided
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                Profile = profileName,
                Subject = cachedToken.Subject
            };

            await _tokenCache.SetAsync(profileName, cacheEntry);

            return tokenResponse;
        }
        catch (HttpRequestException)
        {
            // Refresh failed due to network error, clear the cache
            await _tokenCache.ClearAsync(profileName);
            return null;
        }
        catch (InvalidOperationException)
        {
            // Refresh failed due to invalid response, clear the cache
            await _tokenCache.ClearAsync(profileName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsAuthenticatedAsync(string profileName)
    {
        var token = await GetAccessTokenAsync(profileName);
        return !string.IsNullOrEmpty(token);
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(string profileName)
    {
        await _tokenCache.ClearAsync(profileName);
    }

    /// <inheritdoc/>
    public async Task LogoutAllAsync()
    {
        await _tokenCache.ClearAllAsync();
    }

    /// <summary>
    /// Posts a token request to the authentication endpoint.
    /// </summary>
    private async Task<TokenResponse> PostTokenRequestAsync(string tokenUrl, Dictionary<string, string> formData)
    {
        using var content = new FormUrlEncodedContent(formData);

        var response = await _httpClient.PostAsync(tokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Authentication failed with status {response.StatusCode}: {errorContent}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Invalid token response from server.");
        }

        return tokenResponse;
    }
}
