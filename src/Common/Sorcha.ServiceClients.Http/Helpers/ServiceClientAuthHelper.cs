// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.ServiceClients.Helpers;

/// <summary>
/// Shared helper for setting JWT authentication headers on service client HTTP requests.
/// Eliminates duplicated SetAuthHeaderAsync logic across service clients.
/// </summary>
public static class ServiceClientAuthHelper
{
    /// <summary>
    /// Acquires a service-to-service JWT token and sets the Authorization header on the HTTP client.
    /// </summary>
    /// <param name="httpClient">The HTTP client to set the auth header on.</param>
    /// <param name="authClient">The service auth client for token acquisition.</param>
    /// <param name="logger">Logger for warning when no token is available.</param>
    /// <param name="serviceName">Name of the calling service (used in log messages).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SetAuthHeaderAsync(
        HttpClient httpClient,
        IServiceAuthClient authClient,
        ILogger logger,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var token = await authClient.GetTokenAsync(cancellationToken);
        if (token is not null)
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            logger.LogWarning("No auth token available for {ServiceName} call", serviceName);
        }
    }
}
