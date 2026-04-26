// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// HTTP message handler that attaches the wallet's stored bearer token to
/// every outbound request. Sits at the head of the <see cref="HttpClient"/>
/// pipeline used by <see cref="Sorcha.ServiceClients.CitizenWallet.ICitizenWalletClient"/>
/// and the demo-mint helper. If the token store is empty, requests go out
/// unauthenticated and the server returns 401 — that's the trigger for the
/// UI to surface the sign-in prompt (Feature 114, T109 foundation).
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenStore _store;

    /// <summary>Initialises a new instance.</summary>
    public BearerTokenHandler(IAccessTokenStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var record = await _store.GetAsync(cancellationToken);
        if (record is not null && !string.IsNullOrEmpty(record.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
