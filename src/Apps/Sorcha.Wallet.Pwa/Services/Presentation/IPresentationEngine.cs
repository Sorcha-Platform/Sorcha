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

    /// <summary>
    /// #1195 Phase 2 (Task 7) — choose the RIGHT credential + signing path for the present
    /// <paramref name="surface"/>, so a presentation that cannot verify downstream is never even
    /// attempted (standing requirement: no silent verification failures). A citizen may hold BOTH a
    /// holder-<c>cnf</c> AIAS root (web-issued; presents SERVER-CUSTODY — the wallet service signs the
    /// KB-JWT) AND device-<c>cnf</c> copies bound to specific devices (this device signs).
    ///
    /// <para>Selection rule (design §6):</para>
    /// <list type="bullet">
    /// <item><b>In-person / offline / device-mediated</b> → a device-<c>cnf</c> copy bound to THIS device
    /// (device signs). None on this device but the root is present ⇒
    /// <see cref="PresentationSelectionOutcome.BindDeviceFirst"/> — the UI routes to the Task 6 "Bind to
    /// device" button; NEVER a doomed present.</item>
    /// <item><b>Web / remote / server-mediated</b> → the holder-<c>cnf</c> root (server custody).</item>
    /// <item><b>Auto</b> (default) → prefer a device copy this device can sign for, else the root.</item>
    /// </list>
    ///
    /// <para>The layer is the guard for the recorded trap: device-signing the holder-<c>cnf</c> root
    /// produces a KB-JWT that fails verification downstream with no local error. The root is therefore
    /// NEVER paired with device-signing, and a device copy this device cannot sign for (a DIFFERENT
    /// device's key) is never selected. Discrimination is by RFC 7638 <c>cnf.jwk</c> thumbprint against
    /// BOTH keys — never by key type, which would misclassify a P-256 wallet's EC holder root. A
    /// credential with no readable <c>cnf</c> is legacy/unbound and keeps its Phase-1 device-signed
    /// behaviour (there is no binding for a verifier to check).</para>
    /// </summary>
    /// <param name="request">The parsed present request.</param>
    /// <param name="credentials">The wallet's cached credentials (root + any device copies).</param>
    /// <param name="deviceThumbprint">RFC 7638 thumbprint of THIS device's key
    /// (<c>IDeviceKeyService.GetThumbprintAsync</c>), or <c>null</c> on a host without a usable device
    /// key — in which case no device copy is signable here.</param>
    /// <param name="holderThumbprint">RFC 7638 thumbprint of the citizen's server-custodied holder key
    /// (compute from <c>IHolderKeyClient.GetHolderKeysAsync().HolderJwk</c>), or <c>null</c> when it
    /// could not be resolved — in which case a bound credential that is not THIS device's copy cannot be
    /// classified and selection fails CLOSED with
    /// <see cref="PresentationSelectionOutcome.HolderKeyUnavailable"/> rather than guessing.</param>
    /// <param name="surface">The present surface. Defaults to <see cref="PresentationSurface.Auto"/>.</param>
    PresentationSelection Select(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials,
        string? deviceThumbprint,
        string? holderThumbprint,
        PresentationSurface surface = PresentationSurface.Auto);
}

/// <summary>
/// The present surface, which decides WHICH credential + signing path a presentation uses
/// (#1195 Phase 2, design §6). The tier follows the surface, not the credential the citizen
/// picked — a device copy is device-signed in person; the root is server-custody signed remotely.
/// </summary>
public enum PresentationSurface
{
    /// <summary>
    /// No explicit surface signal: prefer a device-<c>cnf</c> copy this device can sign for, else the
    /// holder-<c>cnf</c> root (server custody). The additive default so existing callers are unaffected.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// In-person / offline / device-mediated present (F185 proximity): REQUIRES a device-<c>cnf</c>
    /// copy bound to this device. No copy but a bindable root present ⇒ <see cref="PresentationSelectionOutcome.BindDeviceFirst"/>.
    /// </summary>
    InPerson = 1,

    /// <summary>
    /// Web / remote / server-mediated present (<c>openid4vp://</c> <c>direct_post</c>): the holder-<c>cnf</c>
    /// root, signed server-custody. Falls back to a this-device copy only when no root is cached.
    /// </summary>
    Remote = 2,
}

/// <summary>Which key signs the KB-JWT for the selected credential (#1195 Phase 2).</summary>
public enum PresentationSigningMode
{
    /// <summary>THIS device's key signs the KB-JWT (<c>IDeviceKeyService.SignAsync</c>) — a device-<c>cnf</c> copy.</summary>
    Device = 0,

    /// <summary>The wallet service signs the KB-JWT under the citizen's holder key (server custody,
    /// <c>POST /api/v1/wallet/presentations/sign-kb</c>) — the holder-<c>cnf</c> root.</summary>
    ServerCustody = 1,
}

/// <summary>The kind of a <see cref="PresentationSelection"/> result.</summary>
public enum PresentationSelectionOutcome
{
    /// <summary>A credential was selected; <see cref="PresentationSelection.Match"/> and
    /// <see cref="PresentationSelection.SigningMode"/> are set.</summary>
    Selected = 0,

    /// <summary>No cached credential can satisfy this request on this surface.</summary>
    NoMatch = 1,

    /// <summary>
    /// An in-person / offline present was requested, the root can satisfy it, but THIS device holds no
    /// device-<c>cnf</c> copy yet. The UI routes to the Task 6 "Bind to device" flow — this is a DISTINCT
    /// outcome, never a present that cannot verify. <see cref="PresentationSelection.RootToBind"/> carries
    /// the root so the UI can deep-link its credential card.
    /// </summary>
    BindDeviceFirst = 2,

    /// <summary>
    /// The citizen's holder-key thumbprint could not be resolved, and the only candidates are bound
    /// credentials that are not THIS device's copy — they cannot be told apart (root vs a foreign
    /// device's copy), so selection fails CLOSED rather than risking a presentation that cannot verify.
    /// A retry after the holder key becomes reachable resolves it.
    /// </summary>
    HolderKeyUnavailable = 3,
}

/// <summary>
/// The result of <see cref="IPresentationEngine.Select"/>: which credential to present and how to sign it,
/// or a distinct non-present outcome. The caller must not re-derive the signing path — pairing the wrong
/// signer with the wrong credential is exactly the failure this layer prevents.
/// </summary>
public sealed record PresentationSelection
{
    /// <summary>The result kind.</summary>
    public required PresentationSelectionOutcome Outcome { get; init; }

    /// <summary>The selected credential + disclosure plan. Non-null only when <see cref="Outcome"/> is
    /// <see cref="PresentationSelectionOutcome.Selected"/>.</summary>
    public CredentialMatch? Match { get; init; }

    /// <summary>How to sign the KB-JWT for <see cref="Match"/>. Meaningful only when
    /// <see cref="Outcome"/> is <see cref="PresentationSelectionOutcome.Selected"/>.</summary>
    public PresentationSigningMode SigningMode { get; init; }

    /// <summary>The holder-<c>cnf</c> root behind a
    /// <see cref="PresentationSelectionOutcome.BindDeviceFirst"/> outcome, so the UI can route straight
    /// to its credential card (the Task 6 bind button surface). Null for every other outcome.</summary>
    public CredentialMatch? RootToBind { get; init; }

    /// <summary>A selected credential + its signing mode.</summary>
    internal static PresentationSelection Selected(CredentialMatch match, PresentationSigningMode mode) =>
        new() { Outcome = PresentationSelectionOutcome.Selected, Match = match, SigningMode = mode };

    /// <summary>Nothing on this device can satisfy the request on this surface.</summary>
    internal static PresentationSelection None { get; } =
        new() { Outcome = PresentationSelectionOutcome.NoMatch };

    /// <summary>The root is presentable but this device must be bound first.</summary>
    internal static PresentationSelection BindFirst(CredentialMatch root) =>
        new() { Outcome = PresentationSelectionOutcome.BindDeviceFirst, RootToBind = root };

    /// <summary>Bound-but-unclassifiable candidates and no holder thumbprint — fail closed.</summary>
    internal static PresentationSelection HolderKeyUnavailable { get; } =
        new() { Outcome = PresentationSelectionOutcome.HolderKeyUnavailable };
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
