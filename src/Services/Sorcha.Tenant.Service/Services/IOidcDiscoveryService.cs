// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Fetches and caches OIDC discovery documents from identity providers.
/// Discovery documents contain endpoint URLs (authorization, token, userinfo, JWKS)
/// needed for OIDC authentication flows.
/// </summary>
public interface IOidcDiscoveryService
{
    /// <summary>
    /// Fetches the OIDC discovery document from the issuer's .well-known/openid-configuration endpoint.
    /// Results are cached in-memory with a 24-hour TTL.
    /// </summary>
    /// <param name="issuerUrl">The OIDC issuer URL (e.g., https://login.microsoftonline.com/{tenant}/v2.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed discovery response with extracted endpoints.</returns>
    /// <exception cref="InvalidOperationException">If the discovery document is unreachable or invalid.</exception>
    Task<DiscoveryResponse> DiscoverAsync(string issuerUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached discovery document, forcing a fresh fetch on next request.
    /// </summary>
    /// <param name="issuerUrl">The issuer URL whose cache entry should be invalidated.</param>
    void InvalidateCache(string issuerUrl);
}
