// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Fetches and caches OIDC discovery documents (.well-known/openid-configuration).
/// Uses IMemoryCache with a 24-hour TTL to avoid repeated HTTP calls.
/// </summary>
public class OidcDiscoveryService : IOidcDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OidcDiscoveryService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private const string CacheKeyPrefix = "oidc_discovery:";

    /// <summary>
    /// Initializes a new instance of <see cref="OidcDiscoveryService"/>.
    /// </summary>
    public OidcDiscoveryService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<OidcDiscoveryService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DiscoveryResponse> DiscoverAsync(string issuerUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerUrl);

        var cacheKey = CacheKeyPrefix + issuerUrl.TrimEnd('/');

        if (_cache.TryGetValue<DiscoveryResponse>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("Returning cached discovery document for {IssuerUrl}", issuerUrl);
            return cached;
        }

        var discoveryUrl = BuildDiscoveryUrl(issuerUrl);
        _logger.LogInformation("Fetching OIDC discovery document from {DiscoveryUrl}", discoveryUrl);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(discoveryUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch discovery document from {DiscoveryUrl}", discoveryUrl);
            throw new InvalidOperationException(
                $"Unable to reach OIDC discovery endpoint at {discoveryUrl}. Verify the issuer URL is correct.", ex);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = ParseDiscoveryDocument(json, issuerUrl);

        _cache.Set(cacheKey, result, CacheDuration);
        _logger.LogInformation(
            "Cached discovery document for {IssuerUrl} (expires in {Hours}h)",
            issuerUrl, CacheDuration.TotalHours);

        return result;
    }

    /// <inheritdoc />
    public void InvalidateCache(string issuerUrl)
    {
        var cacheKey = CacheKeyPrefix + issuerUrl.TrimEnd('/');
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated discovery cache for {IssuerUrl}", issuerUrl);
    }

    private static string BuildDiscoveryUrl(string issuerUrl)
    {
        var baseUrl = issuerUrl.TrimEnd('/');
        return $"{baseUrl}/.well-known/openid-configuration";
    }

    private DiscoveryResponse ParseDiscoveryDocument(string json, string issuerUrl)
    {
        JsonElement doc;
        try
        {
            doc = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid OIDC discovery document from {issuerUrl}. The response is not valid JSON.", ex);
        }

        var issuer = doc.TryGetProperty("issuer", out var issuerProp) ? issuerProp.GetString() : null;
        var authEndpoint = doc.TryGetProperty("authorization_endpoint", out var authProp) ? authProp.GetString() : null;
        var tokenEndpoint = doc.TryGetProperty("token_endpoint", out var tokenProp) ? tokenProp.GetString() : null;
        var userInfoEndpoint = doc.TryGetProperty("userinfo_endpoint", out var userInfoProp) ? userInfoProp.GetString() : null;
        var jwksUri = doc.TryGetProperty("jwks_uri", out var jwksProp) ? jwksProp.GetString() : null;

        var supportedScopes = Array.Empty<string>();
        if (doc.TryGetProperty("scopes_supported", out var scopesProp) && scopesProp.ValueKind == JsonValueKind.Array)
        {
            supportedScopes = scopesProp.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
        }

        if (string.IsNullOrEmpty(issuer))
        {
            throw new InvalidOperationException(
                $"Discovery document from {issuerUrl} is missing the 'issuer' field.");
        }

        return new DiscoveryResponse
        {
            Issuer = issuer,
            AuthorizationEndpoint = authEndpoint,
            TokenEndpoint = tokenEndpoint,
            UserInfoEndpoint = userInfoEndpoint,
            JwksUri = jwksUri,
            SupportedScopes = supportedScopes
        };
    }
}
