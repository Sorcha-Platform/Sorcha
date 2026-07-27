// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Attaches the wallet's bearer token to every outbound request whose destination is the
/// Sorcha gateway itself. On a 401 for a request that carried a token, transparently
/// refreshes once when a refresh token is held (POST /api/auth/token/refresh — re-mints
/// the same tier per F136, so a Consumer refresh stays Consumer) and retries. Only a
/// <em>failed refresh</em> is treated as session death: the token is cleared and
/// <see cref="ISessionExpiryNotifier.NotifyExpired"/> fires so the shell sends the
/// citizen to /signin. A 401 with no refresh token is left untouched — it may be a
/// per-endpoint authorization gap, not an expired session, so the token must NOT be
/// cleared (doing so nukes good sessions); the auth gate handles true clock expiry.
/// </summary>
/// <remarks>
/// Security fix (I1, #1310/#1311 follow-up): several typed clients wired with this handler
/// (e.g. <see cref="Sorcha.Wallet.Pwa.Services.Presentation.IPresentationDirectPostClient"/>)
/// POST to an absolute URI taken from untrusted input — the <c>response_uri</c> of an
/// <c>openid4vp://</c> request the citizen scanned or pasted on <c>/present</c>. Before this
/// fix the handler attached the citizen's bearer token unconditionally, so a malicious QR
/// could harvest it via a third-party <c>response_uri</c>. The gate below is same-origin:
/// the token is attached (and 401-refresh is attempted) ONLY when the outbound request's
/// scheme+host+port matches the gateway's own origin. This lives in the shared handler —
/// not in any one typed client — so no future caller of any client already wired with this
/// handler can accidentally reintroduce the leak by reusing it against a third-party URI.
///
/// This isn't merely a session cookie: Sorcha wallets are server-custodial, so
/// <c>POST /api/v1/wallet/presentations/sign-kb</c> (consumer-tier) uses the bearer token to
/// authorise the wallet service to sign on the citizen's behalf. A leaked token is a leaked
/// signing capability, not just a leaked session — which is why the same-origin gate exists
/// unconditionally here rather than as an opt-in per typed client (#1310).
///
/// Cross-origin <em>redirects</em> are outside this handler's reach — it stamps the header
/// before <see cref="SendAsync"/> hands off to the primary handler, so it never sees a 3xx
/// hop the runtime follows on its behalf. Do NOT "helpfully" add redirect handling here to
/// re-check the origin after a hop: the protection on that path already comes from the
/// runtime, not this handler — the Fetch spec strips <c>Authorization</c> on a cross-origin
/// redirect in the browser, and <see cref="System.Net.Http.SocketsHttpHandler"/> drops it
/// when scheme, host or port change across a redirect. Adding bespoke redirect logic here
/// would only create a second, weaker copy of a guarantee the platform already gives us.
/// </remarks>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenStore _store;
    private readonly HttpClient _refreshHttp;
    private readonly ISessionExpiryNotifier _sessionExpiry;
    private readonly Uri _gatewayOrigin;
    private readonly ILogger<BearerTokenHandler>? _logger;

    // Withheld-bearer origins we've already logged. A presentation flow retries the same
    // foreign response_uri, so logging per-request would flood; this dedupes per distinct
    // foreign origin for the lifetime of the handler instance instead. On the intended
    // attack path (a malicious QR's response_uri), this single Warning line is also the
    // detection signal an operator would search for.
    private readonly ConcurrentDictionary<string, bool> _loggedForeignOrigins = new();

    /// <summary>Initialises a new instance.</summary>
    /// <param name="store">The token store that holds the citizen's JWT and refresh token.</param>
    /// <param name="refreshHttp">
    /// An <see cref="HttpClient"/> pre-configured with the gateway base address used ONLY
    /// for the refresh call. This client MUST NOT have <see cref="BearerTokenHandler"/>
    /// in its pipeline — adding it would create infinite recursion on 401.
    /// </param>
    /// <param name="sessionExpiry">Raised when an unrecoverable 401 clears the stored session.</param>
    /// <param name="gatewayOrigin">
    /// The Sorcha gateway's own origin. The bearer token is attached only to requests whose
    /// resolved <see cref="HttpRequestMessage.RequestUri"/> shares this origin (scheme, host,
    /// port) — see the type-level remarks for why this exists.
    /// </param>
    /// <param name="logger">
    /// Optional logger used to record when the same-origin gate withholds the bearer token.
    /// Nullable so existing test construction (which predates this parameter) keeps working —
    /// production DI always supplies one.
    /// </param>
    public BearerTokenHandler(
        IAccessTokenStore store, HttpClient refreshHttp, ISessionExpiryNotifier sessionExpiry, Uri gatewayOrigin,
        ILogger<BearerTokenHandler>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshHttp = refreshHttp ?? throw new ArgumentNullException(nameof(refreshHttp));
        _sessionExpiry = sessionExpiry ?? throw new ArgumentNullException(nameof(sessionExpiry));
        _gatewayOrigin = gatewayOrigin ?? throw new ArgumentNullException(nameof(gatewayOrigin));
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Same-origin gate: HttpClient resolves a relative RequestUri against its BaseAddress
        // before the handler pipeline runs, so by the time we see it here it is always absolute.
        // A cross-origin destination (an attacker-supplied response_uri) never sees the token.
        var sameOrigin = IsSameOrigin(request.RequestUri, _gatewayOrigin);
        if (!sameOrigin)
        {
            LogWithheldBearerOnce(request.RequestUri);
        }

        var record = sameOrigin ? await _store.GetAsync(ct) : null;
        if (record is not null && !string.IsNullOrEmpty(record.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);

        var response = await base.SendAsync(request, ct);

        // Only react to auth failures on requests that actually carried a token —
        // an anonymous call getting a 401 has no session to refresh or clear.
        if (!sameOrigin || response.StatusCode != HttpStatusCode.Unauthorized
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

    /// <summary>
    /// True when <paramref name="requestUri"/> shares scheme, host and port with
    /// <paramref name="origin"/>. A relative or null <paramref name="requestUri"/> is treated
    /// as NOT same-origin — this handler must never assume trust in the absence of proof.
    /// </summary>
    private static bool IsSameOrigin(Uri? requestUri, Uri origin)
        => requestUri is not null
           && requestUri.IsAbsoluteUri
           && Uri.Compare(
                  requestUri, origin, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
                  StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// Logs a Warning the first time the same-origin gate withholds the bearer token for a
    /// given foreign origin. Deduped per distinct origin (not per request) — a presentation
    /// flow retries against the same foreign <c>response_uri</c>, so a per-request log would
    /// flood, and the first occurrence is already the detection signal that matters. Only the
    /// authority (scheme+host+port) is logged, never the path or query, so nothing from the
    /// (potentially attacker-supplied) URI beyond the origin itself lands in logs.
    /// </summary>
    private void LogWithheldBearerOnce(Uri? requestUri)
    {
        if (_logger is null || requestUri is null || !requestUri.IsAbsoluteUri)
        {
            return;
        }

        var foreignOrigin = requestUri.GetLeftPart(UriPartial.Authority);
        if (!_loggedForeignOrigins.TryAdd(foreignOrigin, true))
        {
            return;
        }

        var gatewayOrigin = _gatewayOrigin.GetLeftPart(UriPartial.Authority);
        _logger.LogWarning(
            "bearer withheld — {RequestOrigin} is not the gateway origin {GatewayOrigin}",
            foreignOrigin, gatewayOrigin);
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
