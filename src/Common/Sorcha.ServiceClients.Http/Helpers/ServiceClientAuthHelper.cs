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
    /// <remarks>
    /// A host that holds NO <c>ServiceAuth:*</c> credential material at all does not authenticate
    /// to backends as itself — it forwards the caller's bearer through a <c>DelegatingHandler</c>
    /// (the public MCP server). For such a host the token demand is skipped and the Authorization
    /// header is left for that handler to stamp. Without this, <c>GetTokenAsync</c> throws on the
    /// first call of every request (the cache is empty, so it always reaches
    /// <c>RequireClientId</c>), each typed client's own <c>catch (Exception)</c> swallows the throw
    /// into a null return, and the tool reports a generic failure having never opened a socket —
    /// which is exactly how ~50 of the MCP server's 64 tools stayed dead behind a green suite.
    /// <para>
    /// A host that IS configured is untouched, including one configured incorrectly: a client id
    /// without a secret still throws out of <c>GetTokenAsync</c> here, unswallowed by this helper.
    /// The two cases must stay distinguishable — collapsing them would turn a fail-closed
    /// credential check into a fail-open one.
    /// </para>
    /// </remarks>
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
        if (authClient.HasNoCredentialsConfigured)
        {
            // NOT the same as "acquisition failed" — this host was never given credentials and
            // never will have any. Demanding a token here kills the request before it is made.
            logger.LogDebug(
                "No ServiceAuth credentials configured for {ServiceName}; leaving the Authorization "
                + "header to the host's caller-token forwarding handler",
                serviceName);
            return;
        }

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
