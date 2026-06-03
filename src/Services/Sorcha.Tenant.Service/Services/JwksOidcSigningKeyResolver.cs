// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Production <see cref="IOidcSigningKeyResolver"/> — fetches the identity provider's JWKS and parses
/// it into signing keys, cached per JWKS URI with a TTL (review M3a). The JWKS URI is taken from
/// <see cref="IdentityProviderConfiguration.JwksUri"/>, falling back to OIDC discovery on the
/// configured issuer. Fetch / parse failures throw so the caller fails closed.
/// </summary>
public sealed class JwksOidcSigningKeyResolver : IOidcSigningKeyResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOidcDiscoveryService _discoveryService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<JwksOidcSigningKeyResolver> _logger;

    /// <summary>Initializes a new instance.</summary>
    public JwksOidcSigningKeyResolver(
        IHttpClientFactory httpClientFactory,
        IOidcDiscoveryService discoveryService,
        IMemoryCache cache,
        ILogger<JwksOidcSigningKeyResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _discoveryService = discoveryService;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        IdentityProviderConfiguration config, bool forceRefresh = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var jwksUri = config.JwksUri;
        if (string.IsNullOrWhiteSpace(jwksUri))
        {
            // Fall back to discovery on the issuer to locate the jwks_uri.
            var discovery = await _discoveryService.DiscoverAsync(config.IssuerUrl, ct);
            jwksUri = discovery.JwksUri;
        }

        if (string.IsNullOrWhiteSpace(jwksUri))
        {
            throw new InvalidOperationException(
                $"No JWKS URI is configured (and none discoverable) for identity provider '{config.IssuerUrl}'. " +
                "ID token signatures cannot be verified.");
        }

        var cacheKey = $"oidc:jwks:{jwksUri}";
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out IReadOnlyCollection<SecurityKey>? cached) && cached is not null)
        {
            return cached;
        }

        var keys = await FetchSigningKeysAsync(jwksUri, ct);
        _cache.Set(cacheKey, keys, CacheTtl);
        return keys;
    }

    private async Task<IReadOnlyCollection<SecurityKey>> FetchSigningKeysAsync(string jwksUri, CancellationToken ct)
    {
        string json;
        try
        {
            var http = _httpClientFactory.CreateClient();
            json = await http.GetStringAsync(jwksUri, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to fetch JWKS from '{jwksUri}'. ID token signatures cannot be verified.", ex);
        }

        try
        {
            var keys = new JsonWebKeySet(json).GetSigningKeys();
            _logger.LogDebug("Fetched {Count} signing key(s) from JWKS {JwksUri}", keys.Count, jwksUri);
            return (IReadOnlyCollection<SecurityKey>)keys;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse JWKS from '{jwksUri}'. ID token signatures cannot be verified.", ex);
        }
    }
}
