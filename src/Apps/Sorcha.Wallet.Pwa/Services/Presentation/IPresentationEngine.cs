// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Presentation;

namespace Sorcha.Wallet.Pwa.Services.Presentation;

/// <summary>
/// Citizen wallet presentation engine (Feature 114, T094). Pure C# — IO-free
/// except via the <paramref name="deviceSigner"/> delegate, so it runs in the
/// WASM sandbox without touching JS interop directly.
///
/// <para>The signer delegate hides the WebCrypto non-extractable device key:
/// callers wire it to <c>IDeviceKeyService.SignAsync</c>, which round-trips through
/// the <c>webcrypto-bridge.js</c> module.</para>
/// </summary>
public interface IPresentationEngine
{
    /// <summary>
    /// Parse an <c>openid4vp://</c> deep link into a structured request. Feature 181 —
    /// the link carries a <c>request_uri</c>; the engine fetches the Request Object via
    /// <paramref name="requestObjectFetcher"/> (the IO-free delegate pattern, like
    /// <c>deviceSigner</c>), decodes its payload, and parses the <c>dcql_query</c>.
    /// The retired inline-<c>presentation_definition</c> form is refused.
    /// </summary>
    /// <param name="openid4vpDeepLink">The scanned/pasted <c>openid4vp://</c> URI.</param>
    /// <param name="requestObjectFetcher">Fetches the request-object JWT text from a URL
    /// (wired to the PWA's HttpClient by the caller).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="System.FormatException">If the link or request object is not a
    /// recognised OpenID4VP 1.0 shape (includes the legacy-dialect refusal).</exception>
    Task<ParsedPresentationRequest> ParseAsync(
        string openid4vpDeepLink,
        Func<string, CancellationToken, Task<string>> requestObjectFetcher,
        CancellationToken ct = default);

    /// <summary>Match the single-ask (first credential query) against the wallet's cached credentials.</summary>
    /// <returns>Empty if no credential satisfies every required claim.</returns>
    IReadOnlyList<CredentialMatch> Match(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials);

    /// <summary>
    /// Match the full DCQL query (Feature 181 US2): per-credential-query candidates plus
    /// <c>credential_sets</c> solving. <see cref="DcqlMatchResult.Satisfiable"/> gates submission —
    /// no partial presentation when any required query/set is unmet.
    /// </summary>
    DcqlMatchResult MatchQuery(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials);

    /// <summary>
    /// Build the on-the-wire <c>vp_token</c> for the chosen credential, including
    /// only the approved disclosure subset and a KB-JWT signed by the device key.
    /// </summary>
    /// <param name="match">Selected credential + capability snapshot from <see cref="Match"/>.</param>
    /// <param name="approvedClaims">Claim names the citizen approved for disclosure (must include all required, may include optional).</param>
    /// <param name="request">The original request (for nonce + audience binding).</param>
    /// <param name="deviceJwk">Public JWK of the device key — embedded as <c>kid</c> identifier in the KB-JWT header.</param>
    /// <param name="deviceSigner">Async delegate that signs raw bytes with the non-extractable WebCrypto device key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> BuildVpTokenAsync(
        CredentialMatch match,
        IReadOnlyList<string> approvedClaims,
        ParsedPresentationRequest request,
        System.Text.Json.JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct = default);

    /// <summary>
    /// Feature 181 US2 — build the multi-credential OpenID4VP 1.0 response envelope: one SD-JWT
    /// presentation per consented credential query, keyed by query id
    /// (<c>{ "&lt;queryId&gt;": ["&lt;presentation&gt;"] }</c>). Each presentation carries a KB-JWT
    /// signed by the device key and only the approved disclosure subset for that query.
    /// </summary>
    /// <param name="consented">The per-query disclosure plan the citizen approved (one entry per
    /// query being presented).</param>
    /// <param name="request">The original request (nonce + audience binding).</param>
    /// <param name="deviceJwk">Public JWK of the device key.</param>
    /// <param name="deviceSigner">Async delegate that signs raw bytes with the device key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The JSON object-keyed <c>vp_token</c> string ready for the direct_post body.</returns>
    Task<string> BuildVpTokenEnvelopeAsync(
        IReadOnlyList<ConsentedQuery> consented,
        ParsedPresentationRequest request,
        System.Text.Json.JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct = default);
}

/// <summary>
/// Feature 181 US2 — one consented credential query in a multi-credential presentation: the chosen
/// credential, the claim names approved for disclosure, and the query's own required-claim set (so
/// each entry is validated against its own ask, not the request-level single-ask convenience).
/// </summary>
/// <param name="QueryId">The DCQL credential-query id — the key of the response envelope entry.</param>
/// <param name="Match">The credential the citizen chose for this query.</param>
/// <param name="RequiredClaims">This query's required claim names.</param>
/// <param name="ApprovedClaims">Claim names approved for disclosure (must cover every required claim).</param>
public sealed record ConsentedQuery(
    string QueryId,
    CredentialMatch Match,
    IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string> ApprovedClaims);
