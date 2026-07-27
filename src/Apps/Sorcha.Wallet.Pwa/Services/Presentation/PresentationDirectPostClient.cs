// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Presentation;

/// <summary>
/// #1310/#1311 — dedicated typed HTTP client for the wallet's OpenID4VP <c>direct_post</c>
/// (the presentation-outcome POST to a verifier's <c>response_uri</c>).
/// </summary>
/// <remarks>
/// <para><c>Pages/Present.razor</c> previously posted this with the ambient
/// <c>@inject HttpClient</c>, which carries NO bearer token — <c>Program.cs</c> registers that
/// client as a bare <c>new HttpClient { BaseAddress = ... }</c> with no
/// <c>BearerTokenHandler</c>. That was invisible while the sorcha-wallet callback's
/// content-type branching bug (#1310) meant every <c>direct_post</c> 415'd before auth was
/// even evaluated. Fixing the 415 exposed the 401 underneath: the callback's
/// <c>[Authorize(RequireConsumerAudience)]</c> policy runs BEFORE the form-vs-json content-type
/// branch, so a bearer-less caller never reaches the code that would have accepted its
/// form-encoded body.</para>
/// <para>This mirrors <see cref="Sorcha.Wallet.Pwa.Services.DeviceBindingService"/>'s own typed
/// <c>HttpClient</c> registration (<c>ServiceCollectionExtensions.AddCitizenWalletServices</c>,
/// wired with <c>.AddHttpMessageHandler&lt;BearerTokenHandler&gt;()</c> +
/// <c>.AddHttpMessageHandler&lt;ServerClockHandler&gt;()</c>) — the one call site that already
/// carried the bearer chain and so never hit this bug. <c>/present</c> is <c>[Authorize]</c>, so
/// a consumer-tier token is already in hand by the time either <c>ConfirmAsync</c> or
/// <c>ConfirmMultiAsync</c> posts.</para>
/// </remarks>
public interface IPresentationDirectPostClient
{
    /// <summary>
    /// POST the <c>direct_post</c> form body (<c>vp_token</c> + <c>state</c>) to the verifier's
    /// <c>response_uri</c>. The URI is absolute — for the <c>sorcha-wallet</c> consumer it is a
    /// same-gateway route, but the client makes no assumption about that; a third-party
    /// verifier's <c>response_uri</c> would be posted exactly the same way.
    /// </summary>
    Task<HttpResponseMessage> PostAsync(
        string responseUri, FormUrlEncodedContent form, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="IPresentationDirectPostClient"/> — a thin wrapper around the typed
/// <see cref="HttpClient"/> DI wires with the bearer + clock handler chain (see
/// <c>ServiceCollectionExtensions.AddCitizenWalletServices</c>).
/// </summary>
public sealed class PresentationDirectPostClient : IPresentationDirectPostClient
{
    private readonly HttpClient _http;

    /// <summary>Initialises a new instance.</summary>
    public PresentationDirectPostClient(HttpClient http)
        => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public Task<HttpResponseMessage> PostAsync(
        string responseUri, FormUrlEncodedContent form, CancellationToken ct = default)
        => _http.PostAsync(responseUri, form, ct);
}
