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

    /// <summary>Match a request against the wallet's cached credentials.</summary>
    /// <returns>Empty if no credential satisfies every required claim.</returns>
    IReadOnlyList<CredentialMatch> Match(
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
}
