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
    /// True when this host holds <em>no</em> service-principal credential material at all —
    /// no <c>ServiceAuth:ClientId</c>, no <c>ServiceAuth:ClientSecret</c> and no workload
    /// certificate.
    /// </summary>
    /// <remarks>
    /// Such a host does not authenticate to backends as itself; it authorises by forwarding the
    /// caller's bearer through a <c>DelegatingHandler</c> (the public MCP server does
    /// exactly this, and deliberately holds no <c>ServiceAuth:*</c> configuration). Callers that
    /// would otherwise demand a service token — see
    /// <c>Sorcha.ServiceClients.Helpers.ServiceClientAuthHelper</c> — must skip the demand rather
    /// than fail, or every typed-client call from such a host dies before a request is made.
    /// <para>
    /// This is deliberately NOT "did token acquisition fail". A host that IS configured and is
    /// broken (client id without secret, unreachable issuer) must still fail loudly through
    /// <see cref="GetTokenAsync"/>; collapsing the two turns a fail-closed credential check into a
    /// fail-open one. Only the total absence of configuration reaches this property.
    /// </para>
    /// <para>
    /// Phrased negatively on purpose: <c>default(bool)</c> is <c>false</c>, so an unconfigured
    /// test double or a future implementation that forgets this member is treated as
    /// <em>credentialed</em> and keeps the demanding, fail-closed path.
    /// </para>
    /// </remarks>
    bool HasNoCredentialsConfigured { get; }

    /// <summary>
    /// Gets a valid JWT token for service-to-service authentication
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWT access token, or null if token acquisition failed</returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
