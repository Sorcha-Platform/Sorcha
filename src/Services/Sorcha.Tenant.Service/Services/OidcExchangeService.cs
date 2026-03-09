// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Implements the OIDC authorization code + PKCE exchange flow.
/// Generates authorization URLs, exchanges codes for tokens, and validates ID tokens.
/// Flow state (nonce, code_verifier, orgId) is stored in IDistributedCache with 10-minute TTL.
/// </summary>
public class OidcExchangeService : IOidcExchangeService
{
    private readonly TenantDbContext _dbContext;
    private readonly IOidcDiscoveryService _discoveryService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly ILogger<OidcExchangeService> _logger;

    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    private const string StateCacheKeyPrefix = "oidc:state:";

    /// <summary>
    /// Initializes a new instance of <see cref="OidcExchangeService"/>.
    /// </summary>
    public OidcExchangeService(
        TenantDbContext dbContext,
        IOidcDiscoveryService discoveryService,
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        ILogger<OidcExchangeService> logger)
    {
        _dbContext = dbContext;
        _discoveryService = discoveryService;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OidcInitiateResponse> GenerateAuthorizationUrlAsync(
        Guid orgId, string? redirectUrl, CancellationToken cancellationToken = default)
    {
        var config = await GetEnabledIdpConfigAsync(orgId, cancellationToken);

        // Generate PKCE code_verifier (43-128 chars, URL-safe)
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ComputeCodeChallenge(codeVerifier);

        // Generate state and nonce
        var state = GenerateRandomString();
        var nonce = GenerateRandomString();

        // Determine redirect URI (callback endpoint on our side)
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);
        var callbackUri = $"/api/auth/callback/{org?.Subdomain ?? orgId.ToString()}";

        // Store flow state in distributed cache
        var stateData = new OidcFlowState
        {
            OrgId = orgId,
            Nonce = nonce,
            CodeVerifier = codeVerifier,
            RedirectUri = callbackUri,
            PostLoginRedirectUrl = redirectUrl,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var stateJson = JsonSerializer.SerializeToUtf8Bytes(stateData);
        await _cache.SetAsync(
            StateCacheKeyPrefix + state,
            stateJson,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateTtl },
            cancellationToken);

        // Build authorization URL
        var scopes = config.Scopes.Length > 0 ? string.Join(" ", config.Scopes) : "openid profile email";
        var authUrl = $"{config.AuthorizationEndpoint}" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(config.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(callbackUri)}" +
            $"&scope={Uri.EscapeDataString(scopes)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&nonce={Uri.EscapeDataString(nonce)}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            $"&code_challenge_method=S256";

        _logger.LogInformation(
            "Generated OIDC authorization URL for org {OrgId} with state {State}",
            orgId, state);

        return new OidcInitiateResponse
        {
            AuthorizationUrl = authUrl,
            State = state
        };
    }

    /// <inheritdoc />
    public async Task<OidcExchangeResult> ExchangeCodeAsync(
        string code, string state, string orgSubdomain, CancellationToken cancellationToken = default)
    {
        // Retrieve and validate flow state from cache
        var stateBytes = await _cache.GetAsync(StateCacheKeyPrefix + state, cancellationToken);
        if (stateBytes is null)
        {
            throw new InvalidOperationException(
                "Invalid or expired state parameter. The OIDC login session may have timed out.");
        }

        var flowState = JsonSerializer.Deserialize<OidcFlowState>(stateBytes)!;

        // Remove state from cache (one-time use)
        await _cache.RemoveAsync(StateCacheKeyPrefix + state, cancellationToken);

        // Load IDP configuration
        var config = await GetEnabledIdpConfigAsync(flowState.OrgId, cancellationToken);
        var clientSecret = IdpConfigurationService.DecryptSecret(config.ClientSecretEncrypted);

        // Exchange authorization code for tokens at the token endpoint
        var httpClient = _httpClientFactory.CreateClient();
        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = flowState.RedirectUri,
            ["client_id"] = config.ClientId,
            ["client_secret"] = clientSecret,
            ["code_verifier"] = flowState.CodeVerifier
        });

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(config.TokenEndpoint, tokenRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach token endpoint for org {OrgId}", flowState.OrgId);
            return new OidcExchangeResult
            {
                Success = false,
                Error = "Unable to reach the identity provider's token endpoint.",
                OrgId = flowState.OrgId
            };
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Token exchange failed for org {OrgId}: {StatusCode} - {Body}",
                flowState.OrgId, response.StatusCode, responseBody);

            var errorMessage = "Token exchange failed.";
            try
            {
                var errorDoc = JsonSerializer.Deserialize<JsonElement>(responseBody);
                if (errorDoc.TryGetProperty("error_description", out var desc))
                    errorMessage = desc.GetString() ?? errorMessage;
                else if (errorDoc.TryGetProperty("error", out var err))
                    errorMessage = err.GetString() ?? errorMessage;
            }
            catch (JsonException)
            {
                // Use default error message
            }

            return new OidcExchangeResult
            {
                Success = false,
                Error = errorMessage,
                OrgId = flowState.OrgId
            };
        }

        // Parse token response
        var tokenDoc = JsonSerializer.Deserialize<JsonElement>(responseBody);
        var idToken = tokenDoc.TryGetProperty("id_token", out var idTokenProp)
            ? idTokenProp.GetString() : null;

        if (string.IsNullOrEmpty(idToken))
        {
            return new OidcExchangeResult
            {
                Success = false,
                Error = "Identity provider did not return an ID token.",
                OrgId = flowState.OrgId
            };
        }

        // Validate ID token (including nonce) and extract claims
        OidcUserClaims claims;
        try
        {
            claims = await ValidateIdTokenAsync(idToken, config, flowState.Nonce, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ID token validation failed for org {OrgId}", flowState.OrgId);
            return new OidcExchangeResult
            {
                Success = false,
                Error = $"ID token validation failed: {ex.Message}",
                OrgId = flowState.OrgId
            };
        }

        _logger.LogInformation(
            "OIDC code exchange successful for org {OrgId}, subject {Subject}",
            flowState.OrgId, claims.Subject);

        return new OidcExchangeResult
        {
            Success = true,
            Claims = claims,
            OrgId = flowState.OrgId
        };
    }

    /// <inheritdoc />
    public Task<OidcUserClaims> ValidateIdTokenAsync(
        string idToken, IdentityProviderConfiguration config, string expectedNonce, CancellationToken cancellationToken = default)
    {
        // Parse JWT payload (base64url-encoded JSON)
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidOperationException("Invalid ID token format: expected 3 JWT segments.");
        }

        JsonElement payload;
        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to decode ID token payload.", ex);
        }

        // Validate issuer
        var issuer = payload.TryGetProperty("iss", out var issProp) ? issProp.GetString() : null;
        if (issuer != config.IssuerUrl)
        {
            throw new InvalidOperationException(
                $"ID token issuer mismatch. Expected '{config.IssuerUrl}', got '{issuer}'.");
        }

        // Validate audience (check all elements when aud is an array)
        if (!AudienceMatches(payload, config.ClientId))
        {
            var audience = GetAudience(payload);
            throw new InvalidOperationException(
                $"ID token audience mismatch. Expected '{config.ClientId}', got '{audience}'.");
        }

        // Validate expiry
        if (payload.TryGetProperty("exp", out var expProp))
        {
            var expiry = DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64());
            if (expiry < DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException(
                    $"ID token has expired at {expiry:O}.");
            }
        }

        // Validate nonce to prevent ID token replay attacks
        var tokenNonce = GetStringClaim(payload, "nonce");
        if (string.IsNullOrEmpty(tokenNonce))
        {
            throw new InvalidOperationException(
                "ID token is missing the 'nonce' claim.");
        }

        if (!string.Equals(tokenNonce, expectedNonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ID token nonce mismatch. Possible replay attack.");
        }

        // TODO: Validate JWT signature against JWKS endpoint in production.
        // For now, we trust the token came from the configured IDP via the token endpoint
        // (which uses TLS and client credentials). This is acceptable per the OIDC spec
        // when the token is received directly from the token endpoint.

        // Extract claims
        var sub = payload.TryGetProperty("sub", out var subProp)
            ? subProp.GetString() : null;

        if (string.IsNullOrEmpty(sub))
        {
            throw new InvalidOperationException("ID token is missing the 'sub' claim.");
        }

        var claims = new OidcUserClaims
        {
            Subject = sub,
            Email = GetStringClaim(payload, "email"),
            EmailVerified = payload.TryGetProperty("email_verified", out var evProp)
                && evProp.ValueKind == JsonValueKind.True,
            DisplayName = GetStringClaim(payload, "name"),
            GivenName = GetStringClaim(payload, "given_name"),
            FamilyName = GetStringClaim(payload, "family_name"),
            Picture = GetStringClaim(payload, "picture"),
            PreferredUsername = GetStringClaim(payload, "preferred_username"),
            Upn = GetStringClaim(payload, "upn")
        };

        _logger.LogDebug(
            "Extracted claims from ID token: sub={Subject}, email={Email}, name={Name}",
            claims.Subject, claims.Email, claims.DisplayName);

        return Task.FromResult(claims);
    }

    #region Private Helpers

    private async Task<IdentityProviderConfiguration> GetEnabledIdpConfigAsync(
        Guid orgId, CancellationToken cancellationToken)
    {
        var config = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.IsEnabled, cancellationToken);

        if (config is null)
        {
            throw new InvalidOperationException(
                $"No enabled identity provider configuration found for organization {orgId}.");
        }

        if (string.IsNullOrEmpty(config.AuthorizationEndpoint))
        {
            throw new InvalidOperationException(
                "IDP authorization endpoint is not configured. Run discovery first.");
        }

        return config;
    }

    private static string GenerateRandomString()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string GenerateCodeVerifier()
    {
        // RFC 7636: 43-128 characters from [A-Z] / [a-z] / [0-9] / "-" / "." / "_" / "~"
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }

    private static string? GetStringClaim(JsonElement payload, string claimName)
    {
        return payload.TryGetProperty(claimName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string? GetAudience(JsonElement payload)
    {
        if (!payload.TryGetProperty("aud", out var audProp))
            return null;

        // Audience can be a string or an array of strings
        return audProp.ValueKind switch
        {
            JsonValueKind.String => audProp.GetString(),
            JsonValueKind.Array => string.Join(", ", audProp.EnumerateArray().Select(a => a.GetString())),
            _ => null
        };
    }

    /// <summary>
    /// Checks whether the given audience payload contains the expected client ID.
    /// When aud is an array, checks all elements (not just the first).
    /// </summary>
    private static bool AudienceMatches(JsonElement payload, string clientId)
    {
        if (!payload.TryGetProperty("aud", out var audProp))
            return false;

        return audProp.ValueKind switch
        {
            JsonValueKind.String => audProp.GetString() == clientId,
            JsonValueKind.Array => audProp.EnumerateArray().Any(a => a.GetString() == clientId),
            _ => false
        };
    }

    #endregion
}

/// <summary>
/// Internal state stored in distributed cache during the OIDC authorization code flow.
/// Keyed by the state parameter: "oidc:state:{state}".
/// </summary>
internal record OidcFlowState
{
    public Guid OrgId { get; init; }
    public string Nonce { get; init; } = string.Empty;
    public string CodeVerifier { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string? PostLoginRedirectUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
