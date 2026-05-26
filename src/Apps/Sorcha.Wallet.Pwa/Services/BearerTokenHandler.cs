// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Attaches the wallet's bearer token to every outbound request. On a 401 with a
/// stored refresh token, transparently refreshes once (POST /api/auth/token/refresh —
/// re-mints the same tier per F136, so a Consumer refresh stays Consumer) and
/// retries. A failed refresh clears the session so the gate sends the citizen to
/// /signin on the next navigation.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenStore _store;
    private readonly HttpClient _refreshHttp;

    /// <summary>Initialises a new instance.</summary>
    /// <param name="store">The token store that holds the citizen's JWT and refresh token.</param>
    /// <param name="refreshHttp">
    /// An <see cref="HttpClient"/> pre-configured with the gateway base address used ONLY
    /// for the refresh call. This client MUST NOT have <see cref="BearerTokenHandler"/>
    /// in its pipeline — adding it would create infinite recursion on 401.
    /// </param>
    public BearerTokenHandler(IAccessTokenStore store, HttpClient refreshHttp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshHttp = refreshHttp ?? throw new ArgumentNullException(nameof(refreshHttp));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var record = await _store.GetAsync(ct);
        if (record is not null && !string.IsNullOrEmpty(record.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized
            || string.IsNullOrEmpty(record?.RefreshToken))
        {
            return response;
        }

        var refreshed = await TryRefreshAsync(record.RefreshToken, record.Email, ct);
        if (refreshed is null)
        {
            await _store.ClearAsync(ct); // gate redirects to /signin on next navigation
            return response;
        }

        response.Dispose();
        var retry = await CloneAsync(request, ct);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, ct);
    }

    private async Task<AccessTokenRecord?> TryRefreshAsync(
        string refreshToken, string? email, CancellationToken ct)
    {
        try
        {
            var resp = await _refreshHttp.PostAsJsonAsync(
                "api/auth/token/refresh", new RefreshBody(refreshToken), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<RefreshResponse>(ct);
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
