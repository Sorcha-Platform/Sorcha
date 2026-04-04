// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Client interface for service-to-service JWT token acquisition
/// </summary>
/// <remarks>
/// Implementations acquire tokens via OAuth2 client_credentials grant
/// from the Tenant Service. Tokens are cached and refreshed automatically.
/// </remarks>
public interface IServiceAuthClient
{
    /// <summary>
    /// Gets a valid JWT token for service-to-service authentication
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT access token, or null if token acquisition failed</returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
