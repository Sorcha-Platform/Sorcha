// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Attaches the wallet's bearer token to every outbound request. On a 401 for a
/// request that carried a token, transparently refreshes once when a refresh
/// token is held (POST /api/auth/token/refresh — re-mints the same tier per F136,
/// so a Consumer refresh stays Consumer) and retries. Only a <em>failed refresh</em>
/// is treated as session death: the token is cleared and
/// <see cref="ISessionExpiryNotifier.NotifyExpired"/> fires so the shell sends the
/// citizen to /signin. A 401 with no refresh token is left untouched — it may be a
/// per-endpoint authorization gap, not an expired session, so the token must NOT be
/// cleared (doing so nukes good sessions); the auth gate handles true clock expiry.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenStore _store;
    private readonly HttpClient _refreshHttp;
    private readonly ISessionExpiryNotifier _sessionExpiry;

    /// <summary>Initialises a new instance.</summary>
    /// <param name="store">The token store that holds the citizen's JWT and refresh token.</param>
    /// <param name="refreshHttp">
    /// An <see cref="HttpClient"/> pre-configured with the gateway base address used ONLY
    /// for the refresh call. This client MUST NOT have <see cref="BearerTokenHandler"/>
    /// in its pipeline — adding it would create infinite recursion on 401.
    /// </param>
    /// <param name="sessionExpiry">Raised when an unrecoverable 401 clears the stored session.</param>
    public BearerTokenHandler(IAccessTokenStore store, HttpClient refreshHttp, ISessionExpiryNotifier sessionExpiry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshHttp = refreshHttp ?? throw new ArgumentNullException(nameof(refreshHttp));
        _sessionExpiry = sessionExpiry ?? throw new ArgumentNullException(nameof(sessionExpiry));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var record = await _store.GetAsync(ct);
        if (record is not null && !string.IsNullOrEmpty(record.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);

        var response = await base.SendAsync(request, ct);

        // Only react to auth failures on requests that actually carried a token —
        // an anonymous call getting a 401 has no session to refresh or clear.
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || record is null || string.IsNullOrEmpty(record.AccessToken))
        {
            return response;
        }

        // With no refresh token we CANNOT conclude the session is dead: a 401 from a
        // single endpoint can be a per-endpoint authorization gap (e.g. a consumer-tier
        // token hitting a platform-only resource), and the token may still be valid for
        // the wallet's own surfaces. Leave it untouched — the auth gate
        // (WalletAuthenticationStateProvider) already redirects to /signin once the
        // token expires by clock. Clearing here on every 401 nukes good sessions.
        if (string.IsNullOrEmpty(record.RefreshToken))
        {
            return response;
        }

        // We hold a refresh token: a 401 means the access token should be re-minted.
        var refreshed = await TryRefreshAsync(record.RefreshToken, record.Email, ct);
        if (refreshed is not null)
        {
            response.Dispose();
            var retry = await CloneAsync(request, ct);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
            return await base.SendAsync(retry, ct);
        }

        // Refresh failed → the token can't be renewed, so the session really is dead.
        // Clear it and signal the shell to redirect to /signin now.
        await _store.ClearAsync(ct);
        _sessionExpiry.NotifyExpired();
        return response;
    }

    // No refresh lock: WASM is single-threaded. Concurrent 401s can interleave across awaits and
    // double-refresh, but rotating-token servers grant a grace window, so the worst case is one
    // wasteful refresh call — not a stuck session. A SemaphoreSlim guard isn't worth the complexity here.
    private async Task<AccessTokenRecord?> TryRefreshAsync(
        string refreshToken, string? email, CancellationToken ct)
    {
        try
        {
            var resp = await _refreshHttp.PostAsJsonAsync(
                "api/auth/token/refresh", new RefreshBody(refreshToken), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<RefreshResponse>(JsonDefaults.Api, ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken)) return null;

            var record = new AccessTokenRecord(
                body.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, body.ExpiresIn)),
                email,
                string.IsNullOrEmpty(body.RefreshToken) ? refreshToken : body.RefreshToken);
            await _store.SetAsync(record, ct);
            return record;
        }
        catch { return null; }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage req, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri) { Version = req.Version };
        foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }

    private sealed record RefreshBody(
        [property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
