// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Verifier.Services;

/// <summary>
/// Attaches a service-tier bearer token to outbound HAIP verifier requests (Feature 164 / #1189).
/// The Open Verifier authenticates to the authenticated HAIP create-request + result endpoints as
/// the <c>service-verifier</c> service principal via <see cref="IServiceAuthClient"/>.
/// </summary>
public sealed class ServiceAuthMessageHandler(IServiceAuthClient authClient) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await authClient.GetTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
