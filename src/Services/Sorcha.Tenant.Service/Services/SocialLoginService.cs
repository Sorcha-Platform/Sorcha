// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Configuration for a social login provider.
/// </summary>
public class SocialProviderConfig
{
    /// <summary>
    /// Provider name (e.g., "Google", "Microsoft", "GitHub", "Apple").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Authorization endpoint URL. If null, well-known defaults are used for recognized providers.
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// Token endpoint URL. If null, well-known defaults are used for recognized providers.
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// User info endpoint URL. If null, well-known defaults are used for recognized providers.
    /// </summary>
    public string? UserInfoEndpoint { get; set; }

    /// <summary>
    /// OAuth2 scopes to request.
    /// </summary>
    public List<string> Scopes { get; set; } = ["openid", "email", "profile"];
}

/// <summary>
/// Service implementation for public user social login via OAuth2/OIDC providers.
/// Supports Google, Microsoft, GitHub, and Apple with well-known endpoint defaults.
/// Uses PKCE and state parameters for secure authorization flows.
/// </summary>
public class SocialLoginService : ISocialLoginService
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private static readonly Dictionary<string, (string Auth, string Token, string UserInfo)> WellKnownEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google"] = (
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            "https://openidconnect.googleapis.com/v1/userinfo"),
        ["Microsoft"] = (
            "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "https://graph.microsoft.com/oidc/userinfo"),
        ["GitHub"] = (
            "https://github.com/login/oauth/authorize",
            "https://github.com/login/oauth/access_token",
            "https://api.github.com/user"),
        ["Apple"] = (
            "https://appleid.apple.com/auth/authorize",
            "https://appleid.apple.com/auth/token",
            "https://appleid.apple.com/auth/userinfo"),
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SocialLoginService> _logger;
    private readonly Dictionary<string, SocialProviderConfig> _providers;

    /// <summary>
    /// Creates a new <see cref="SocialLoginService"/> instance.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for making provider API calls.</param>
    /// <param name="cache">Distributed cache for storing state and PKCE verifiers.</param>
    /// <param name="configuration">Application configuration containing the SocialProviders section.</param>
    /// <param name="logger">Logger instance.</param>
    public SocialLoginService(
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        IConfiguration configuration,
        ILogger<SocialLoginService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;

        var providerConfigs = configuration.GetSection("SocialProviders").Get<List<SocialProviderConfig>>() ?? [];
        _providers = new Dictionary<string, SocialProviderConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in providerConfigs)
        {
            if (!string.IsNullOrWhiteSpace(config.Name))
            {
                _providers[config.Name] = config;
            }
        }
    }

    /// <inheritdoc />
    public async Task<SocialAuthInitiateResult> GenerateAuthorizationUrlAsync(
        string provider,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(redirectUri);

        var config = GetProviderConfig(provider);
        var (authEndpoint, _, _) = GetEndpoints(provider, config);

        // Generate PKCE code verifier and challenge
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Generate state for CSRF protection
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        // Cache state + code verifier + redirect URI for validation during callback
        var stateData = JsonSerializer.Serialize(new SocialStateData
        {
            CodeVerifier = codeVerifier,
            RedirectUri = redirectUri,
            Provider = provider
        });

        var cacheKey = $"social:state:{state}";
        await _cache.SetAsync(
            cacheKey,
            Encoding.UTF8.GetBytes(stateData),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateTtl },
            cancellationToken);

        // Build authorization URL
        var scopes = string.Join(" ", config.Scopes);
        var authUrl = $"{authEndpoint}?" +
            $"client_id={Uri.EscapeDataString(config.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256";

        _logger.LogInformation("Social login authorization URL generated for provider {Provider}", provider);

        return new SocialAuthInitiateResult(authUrl, state);
    }

    /// <inheritdoc />
    public async Task<SocialAuthCallbackResult> ExchangeCodeAsync(
        string provider,
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(state);

        // Validate state from cache
        var cacheKey = $"social:state:{state}";
        var stateBytes = await _cache.GetAsync(cacheKey, cancellationToken);
        if (stateBytes is null)
        {
            _logger.LogWarning("Social login callback: invalid or expired state parameter");
            return new SocialAuthCallbackResult(false, "Invalid or expired state parameter.", null, null, null, provider);
        }

        // Remove state from cache (single-use)
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        var stateData = JsonSerializer.Deserialize<SocialStateData>(Encoding.UTF8.GetString(stateBytes));
        if (stateData is null || !string.Equals(stateData.Provider, provider, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Social login callback: state provider mismatch");
            return new SocialAuthCallbackResult(false, "State parameter mismatch.", null, null, null, provider);
        }

        var config = GetProviderConfig(provider);
        var (_, tokenEndpoint, userInfoEndpoint) = GetEndpoints(provider, config);

        try
        {
            // Exchange code for tokens
            var tokenResponse = await ExchangeCodeForTokensAsync(
                config, tokenEndpoint, code, stateData.RedirectUri, stateData.CodeVerifier, cancellationToken);

            if (tokenResponse is null)
            {
                return new SocialAuthCallbackResult(false, "Token exchange failed.", null, null, null, provider);
            }

            // Extract claims — GitHub uses user info endpoint, others use ID token
            if (string.Equals(provider, "GitHub", StringComparison.OrdinalIgnoreCase))
            {
                return await ExtractGitHubClaimsAsync(tokenResponse.Value, userInfoEndpoint, provider, cancellationToken);
            }

            return await ExtractOidcClaimsAsync(tokenResponse.Value, userInfoEndpoint, provider, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Social login code exchange failed for provider {Provider}", provider);
            return new SocialAuthCallbackResult(false, "Authentication failed.", null, null, null, provider);
        }
    }

    private SocialProviderConfig GetProviderConfig(string provider)
    {
        if (!_providers.TryGetValue(provider, out var config))
        {
            throw new ArgumentException($"Social provider '{provider}' is not configured.", nameof(provider));
        }

        return config;
    }

    private static (string Auth, string Token, string UserInfo) GetEndpoints(string provider, SocialProviderConfig config)
    {
        // Use configured endpoints, falling back to well-known defaults
        string authEndpoint = config.AuthorizationEndpoint ?? string.Empty;
        string tokenEndpoint = config.TokenEndpoint ?? string.Empty;
        string userInfoEndpoint = config.UserInfoEndpoint ?? string.Empty;

        if (WellKnownEndpoints.TryGetValue(provider, out var wellKnown))
        {
            if (string.IsNullOrEmpty(authEndpoint)) authEndpoint = wellKnown.Auth;
            if (string.IsNullOrEmpty(tokenEndpoint)) tokenEndpoint = wellKnown.Token;
            if (string.IsNullOrEmpty(userInfoEndpoint)) userInfoEndpoint = wellKnown.UserInfo;
        }

        if (string.IsNullOrEmpty(authEndpoint) || string.IsNullOrEmpty(tokenEndpoint))
        {
            throw new InvalidOperationException(
                $"Social provider '{provider}' requires AuthorizationEndpoint and TokenEndpoint configuration.");
        }

        return (authEndpoint, tokenEndpoint, userInfoEndpoint);
    }

    private async Task<TokenExchangeResult?> ExchangeCodeForTokensAsync(
        SocialProviderConfig config,
        string tokenEndpoint,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["code_verifier"] = codeVerifier,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        // GitHub requires Accept: application/json
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Token exchange failed with status {StatusCode}: {Body}",
                response.StatusCode, errorBody);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;

        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Token exchange returned no access_token");
            return null;
        }

        return new TokenExchangeResult(accessToken, idToken);
    }

    private async Task<SocialAuthCallbackResult> ExtractOidcClaimsAsync(
        TokenExchangeResult tokens,
        string userInfoEndpoint,
        string provider,
        CancellationToken cancellationToken)
    {
        // Try ID token first for claims
        if (!string.IsNullOrEmpty(tokens.IdToken))
        {
            var claims = ParseIdTokenClaims(tokens.IdToken);
            if (claims is not null)
            {
                _logger.LogInformation("Social login claims extracted from ID token for provider {Provider}", provider);
                return new SocialAuthCallbackResult(true, null, claims.Subject, claims.Email, claims.DisplayName, provider);
            }
        }

        // Fall back to user info endpoint
        if (!string.IsNullOrEmpty(userInfoEndpoint))
        {
            return await FetchUserInfoClaimsAsync(tokens.AccessToken, userInfoEndpoint, provider, cancellationToken);
        }

        _logger.LogWarning("No ID token or user info endpoint available for provider {Provider}", provider);
        return new SocialAuthCallbackResult(false, "Could not extract user claims.", null, null, null, provider);
    }

    private async Task<SocialAuthCallbackResult> ExtractGitHubClaimsAsync(
        TokenExchangeResult tokens,
        string userInfoEndpoint,
        string provider,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sorcha", "1.0"));

        // Fetch user profile
        using var userResponse = await client.GetAsync(userInfoEndpoint, cancellationToken);
        if (!userResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub user info request failed with status {StatusCode}", userResponse.StatusCode);
            return new SocialAuthCallbackResult(false, "Failed to fetch GitHub user info.", null, null, null, provider);
        }

        var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
        using var userDoc = JsonDocument.Parse(userJson);
        var userRoot = userDoc.RootElement;

        var subject = userRoot.TryGetProperty("id", out var id) ? id.ToString() : null;
        var displayName = userRoot.TryGetProperty("name", out var name) ? name.GetString() : null;
        var login = userRoot.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;
        displayName ??= login;

        // Fetch primary verified email from /user/emails
        string? email = null;
        try
        {
            using var emailResponse = await client.GetAsync("https://api.github.com/user/emails", cancellationToken);
            if (emailResponse.IsSuccessStatusCode)
            {
                var emailJson = await emailResponse.Content.ReadAsStringAsync(cancellationToken);
                using var emailDoc = JsonDocument.Parse(emailJson);
                foreach (var emailEntry in emailDoc.RootElement.EnumerateArray())
                {
                    var isPrimary = emailEntry.TryGetProperty("primary", out var p) && p.GetBoolean();
                    var isVerified = emailEntry.TryGetProperty("verified", out var v) && v.GetBoolean();
                    if (isPrimary && isVerified)
                    {
                        email = emailEntry.TryGetProperty("email", out var e) ? e.GetString() : null;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub user emails");
        }

        _logger.LogInformation("GitHub social login claims extracted for user {Subject}", subject);
        return new SocialAuthCallbackResult(true, null, subject, email, displayName, provider);
    }

    private async Task<SocialAuthCallbackResult> FetchUserInfoClaimsAsync(
        string accessToken,
        string userInfoEndpoint,
        string provider,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync(userInfoEndpoint, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("User info request failed for provider {Provider} with status {StatusCode}",
                provider, response.StatusCode);
            return new SocialAuthCallbackResult(false, "Failed to fetch user info.", null, null, null, provider);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
        var displayName = root.TryGetProperty("name", out var n) ? n.GetString() : null;

        _logger.LogInformation("Social login user info claims extracted for provider {Provider}", provider);
        return new SocialAuthCallbackResult(true, null, subject, email, displayName, provider);
    }

    private static IdTokenClaims? ParseIdTokenClaims(string idToken)
    {
        // Decode the JWT payload (second segment) without validation
        // Token signature was already validated by the provider during the exchange
        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = parts[1];
            // Pad base64url to standard base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;

            var subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var displayName = root.TryGetProperty("name", out var n) ? n.GetString() : null;

            return new IdTokenClaims(subject, email, displayName);
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private record SocialStateData
    {
        public string CodeVerifier { get; init; } = string.Empty;
        public string RedirectUri { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
    }

    private readonly record struct TokenExchangeResult(string AccessToken, string? IdToken);

    private record IdTokenClaims(string? Subject, string? Email, string? DisplayName);
}
