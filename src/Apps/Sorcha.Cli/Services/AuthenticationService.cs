// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Json;
using System.Text.Json;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Tenant.Models.Auth;

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
    public async Task<TokenResponse> LoginAsync(UserLoginRequest request, string profileName, Guid? organizationId = null)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(request));
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

        var loginUrl = profile.GetUserLoginUrl();
        if (string.IsNullOrEmpty(loginUrl))
        {
            throw new InvalidOperationException(
                $"Profile '{profileName}' does not have a Tenant Service URL configured.");
        }

        var outcome = await PostUserLoginRequestAsync(loginUrl, request);

        if (outcome.OrgSelection is { } orgSelection)
        {
            if (organizationId is { } selectedOrgId)
            {
                return await CompleteOrgSelectionAsync(orgSelection.PlatformLoginToken, selectedOrgId, profileName, request.Email);
            }

            throw new OrgSelectionRequiredException(orgSelection.PlatformLoginToken, orgSelection.Organizations);
        }

        var tokenResponse = outcome.Token!;
        await CacheTokenAsync(profileName, request.Email, tokenResponse);
        return tokenResponse;
    }

    /// <inheritdoc/>
    public async Task<TokenResponse> CompleteOrgSelectionAsync(string platformLoginToken, Guid organizationId, string profileName, string subjectEmail)
    {
        if (string.IsNullOrWhiteSpace(platformLoginToken))
        {
            throw new ArgumentException("Platform login token cannot be empty.", nameof(platformLoginToken));
        }

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
        }

        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var selectOrgUrl = profile.GetOrgSelectUrl();
        if (string.IsNullOrEmpty(selectOrgUrl))
        {
            throw new InvalidOperationException(
                $"Profile '{profileName}' does not have a Tenant Service URL configured.");
        }

        var request = new CompleteOrgSelectionRequest
        {
            PlatformLoginToken = platformLoginToken,
            OrganizationId = organizationId
        };

        using var response = await _httpClient.PostAsJsonAsync(selectOrgUrl, request, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Organization selection failed with status {response.StatusCode}: {body}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        if (tokenResponse == null || !tokenResponse.IsValid())
        {
            throw new InvalidOperationException("Invalid token response from server.");
        }

        await CacheTokenAsync(profileName, subjectEmail, tokenResponse);
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

        // Service-principal (client_credentials) minting is an INTERNAL-only Tenant Service
        // operation as of #1397/#1406 — the public API Gateway does not route
        // /api/internal/service-auth/token. This only succeeds when the CLI runs inside the Sorcha
        // trust network (or the profile points ServiceAuthTokenUrl directly at the Tenant Service).
        var serviceAuthUrl = profile.GetServiceAuthTokenUrl();
        if (string.IsNullOrEmpty(serviceAuthUrl))
        {
            throw new InvalidOperationException(
                $"Profile '{profileName}' does not have a Tenant Service URL configured.");
        }

        TokenResponse tokenResponse;
        try
        {
            tokenResponse = await PostTokenRequestAsync(serviceAuthUrl, formData);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Service-principal authentication targets the Tenant Service's INTERNAL-only token "
                + $"endpoint ({serviceAuthUrl}), which the public API Gateway does not route (#1397). "
                + "This succeeds only when the CLI runs inside the Sorcha trust network (e.g. a "
                + "container on the same docker-compose network) or the profile's ServiceAuthTokenUrl "
                + "points directly at a reachable Tenant Service. If you are running the CLI from "
                + "outside that boundary, service-principal login is not available here — use a "
                + $"co-located CLI or automation instead. Underlying error: {ex.Message}", ex);
        }

        await CacheTokenAsync(profileName, request.ClientId, tokenResponse);

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

    /// <summary>
    /// Posts the JSON user-login request to <c>POST /api/auth/login</c> and discriminates the
    /// three possible response shapes: a direct token (single-org account), an org-selection
    /// response (multi-org account, see <see cref="OrgSelectionResponse"/>), or a two-factor
    /// challenge (not yet supported by this CLI login flow — surfaced as a clear error rather than
    /// silently mishandled).
    /// </summary>
    private async Task<LoginOutcome> PostUserLoginRequestAsync(string loginUrl, UserLoginRequest request)
    {
        using var response = await _httpClient.PostAsJsonAsync(loginUrl, request, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login failed with status {response.StatusCode}: {body}");
        }

        using (var document = JsonDocument.Parse(body))
        {
            var root = document.RootElement;

            if (root.TryGetProperty("requires_org_selection", out var orgFlag)
                && orgFlag.ValueKind == JsonValueKind.True)
            {
                var orgSelection = JsonSerializer.Deserialize<OrgSelectionResponse>(body, JsonOptions);
                if (orgSelection == null || string.IsNullOrEmpty(orgSelection.PlatformLoginToken))
                {
                    throw new InvalidOperationException("Invalid org-selection response from server.");
                }

                return new LoginOutcome(null, orgSelection);
            }

            if (root.TryGetProperty("requires_two_factor", out var totpFlag)
                && totpFlag.ValueKind == JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    "This account requires two-factor authentication, which 'sorcha auth login' does "
                    + "not yet support. Complete 2FA via the web UI, or disable it for automation "
                    + "accounts.");
            }
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        if (tokenResponse == null || !tokenResponse.IsValid())
        {
            throw new InvalidOperationException("Invalid token response from server.");
        }

        return new LoginOutcome(tokenResponse, null);
    }

    /// <summary>
    /// Caches a successful token response for a profile.
    /// </summary>
    private async Task CacheTokenAsync(string profileName, string subject, TokenResponse tokenResponse)
    {
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
            Profile = profileName,
            Subject = subject
        };

        await _tokenCache.SetAsync(profileName, cacheEntry);
    }

    /// <summary>
    /// The two possible successful outcomes of <see cref="PostUserLoginRequestAsync"/> — a direct
    /// token, or an org-selection challenge. Exactly one is non-null.
    /// </summary>
    private sealed record LoginOutcome(TokenResponse? Token, OrgSelectionResponse? OrgSelection);
}
