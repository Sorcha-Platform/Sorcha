// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.UI.Core.Models.Presentation;

/// <summary>Parsed shape of an <c>openid4vp://</c> cross-device deep link.</summary>
public sealed record ParsedPresentationRequest
{
    /// <summary>The verifier's audience claim — wallet's KB-JWT must echo this in <c>aud</c>.</summary>
    public required string ClientId { get; init; }

    /// <summary>URL the wallet POSTs the vp_token to (response_uri).</summary>
    public required string ResponseUri { get; init; }

    /// <summary>Verifier-supplied nonce — wallet's KB-JWT must echo it.</summary>
    public required string Nonce { get; init; }

    /// <summary>
    /// The full DCQL query (Feature 181 US2 — the multi-credential source of truth).
    /// The single-query convenience fields below are derived from <c>Query.Credentials[0]</c>
    /// for the single-ask flow; the per-query <see cref="Match"/> path consumes <see cref="Query"/> directly.
    /// </summary>
    public required DcqlQuery Query { get; init; }

    /// <summary>Required credential type URI (first credential query's vct — single-ask convenience).</summary>
    public required string RequiredVct { get; init; }

    /// <summary>Mandatory claim names the wallet must disclose (first credential query — single-ask convenience).</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Optional claim names the wallet may disclose (first credential query — single-ask convenience).</summary>
    public required IReadOnlyList<string> OptionalClaims { get; init; }

    /// <summary>Display purpose extracted from the request.</summary>
    public string? Purpose { get; init; }

    /// <summary>Response mode; v1 supports <c>direct_post</c>.</summary>
    public string ResponseMode { get; init; } = "direct_post";

    /// <summary>
    /// Feature 181 US6 — the three-state verifier-authentication verdict rendered on the consent
    /// surface. Set by the presentation engine after validating the signed request object; a HARD
    /// refusal (tampered signature / host mismatch) stops the parse before this is ever populated,
    /// so a non-refused request always carries a state (Trusted / AuthenticUntrusted / Unverifiable).
    /// Null only for requests parsed before US6 wiring.
    /// </summary>
    public VerifierAuthState? VerifierAuthentication { get; init; }
}

/// <summary>
/// Per-credential-query match (Feature 181 US2): a single ask inside the DCQL request plus every
/// cached credential that can satisfy it. A query is satisfiable when it has at least one candidate.
/// </summary>
public sealed record DcqlQueryMatch
{
    /// <summary>The DCQL credential-query id (the key the response envelope is keyed by).</summary>
    public required string QueryId { get; init; }

    /// <summary>The credential type asked for (display).</summary>
    public required string Vct { get; init; }

    /// <summary>Required claim names for this query.</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Optional claim names for this query.</summary>
    public required IReadOnlyList<string> OptionalClaims { get; init; }

    /// <summary>Cached credentials that satisfy this query's required claims (may be several — the citizen chooses).</summary>
    public required IReadOnlyList<CredentialMatch> Candidates { get; init; }

    /// <summary>Whether at least one cached credential satisfies this query.</summary>
    public bool IsSatisfiable => Candidates.Count > 0;
}

/// <summary>
/// A DCQL <c>credential_sets</c> entry projected for the consent surface (Feature 181 US2): the
/// alternative id-combinations that satisfy the set, and which of them the wallet can actually fulfil.
/// </summary>
public sealed record DcqlSetChoice
{
    /// <summary>Every option the verifier offered — each is a list of credential-query ids that together satisfy the set.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Options { get; init; }

    /// <summary>The subset of <see cref="Options"/> the wallet can satisfy from its cached credentials.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> SatisfiableOptions { get; init; }

    /// <summary>Whether satisfying this set is required.</summary>
    public required bool Required { get; init; }

    /// <summary>Consent-surface display text.</summary>
    public string? Purpose { get; init; }

    /// <summary>A required set is met when at least one of its options is fully satisfiable.</summary>
    public bool IsMet => !Required || SatisfiableOptions.Count > 0;
}

/// <summary>
/// The request-level match verdict (Feature 181 US2). <see cref="Satisfiable"/> is the gate on
/// submission — no partial presentation is offered when any required query/set is unmet (FR-006d).
/// </summary>
public sealed record DcqlMatchResult
{
    /// <summary>Whether every required credential query / credential-set is satisfiable.</summary>
    public required bool Satisfiable { get; init; }

    /// <summary>Per-credential-query candidates, in request order.</summary>
    public required IReadOnlyList<DcqlQueryMatch> PerQuery { get; init; }

    /// <summary>The required query ids the wallet cannot satisfy (drives the "missing credential" surface).</summary>
    public required IReadOnlyList<string> UnsatisfiedRequiredQueryIds { get; init; }

    /// <summary>Projected <c>credential_sets</c> alternatives (empty when the request declares none → all queries required).</summary>
    public required IReadOnlyList<DcqlSetChoice> SetChoices { get; init; }
}

/// <summary>Which credential format a cached credential is (Feature 185).</summary>
/// <remarks>
/// Before feature 185 the wallet held only SD-JWT VCs, so no discriminator was needed. Proximity presentation
/// is mdoc-shaped, so the cache now carries both — and the matching rules differ per format (see
/// <see cref="CachedCredential"/>).
/// </remarks>
public enum CachedCredentialFormat
{
    /// <summary>SD-JWT VC (<c>dc+sd-jwt</c>) — the Sorcha-native rail. Identified by <c>vct</c>.</summary>
    SdJwtVc = 0,

    /// <summary>ISO 18013-5 mdoc (<c>mso_mdoc</c>) — the proximity/EUDI rail. Identified by <c>docType</c>.</summary>
    MsoMdoc = 1
}

/// <summary>
/// A credential present in the wallet's cache, in the shape the presentation engine needs.
/// In production this is materialised by <c>ICredentialCache</c> on demand from IndexedDB.
/// </summary>
/// <remarks>
/// <b>Two formats, two identifiers.</b> An SD-JWT VC is identified by its <see cref="Vct"/>; an mdoc by its
/// <see cref="DocType"/>. They are different strings and are <em>not</em> interchangeable — a request for
/// <c>org.iso.18013.5.1.mDL</c> must never be satisfied by matching it against a <c>vct</c>. Which field
/// applies is decided by <see cref="Format"/>, and nothing else.
/// </remarks>
public sealed record CachedCredential
{
    /// <summary>Credential id (UUID assigned by the issuer).</summary>
    public required Guid Id { get; init; }

    /// <summary>Which rail this credential is on. Decides how it is matched and how it is presented.</summary>
    public CachedCredentialFormat Format { get; init; } = CachedCredentialFormat.SdJwtVc;

    /// <summary>Credential type URI. <b>SD-JWT only</b> — empty for an mdoc.</summary>
    public string Vct { get; init; } = string.Empty;

    /// <summary>
    /// mdoc document type, e.g. <c>org.iso.18013.5.1.mDL</c>. <b>mdoc only</b> — null for an SD-JWT VC.
    /// </summary>
    public string? DocType { get; init; }

    /// <summary>
    /// SD-JWT VC compact form: <c>credentialJwt~disclosure1~..~disclosureN</c>.
    /// Trailing tilde is preserved if present. Does NOT include a KB-JWT — that's
    /// minted at presentation time. <b>SD-JWT only</b> — empty for an mdoc.
    /// </summary>
    public string RawSdJwt { get; init; } = string.Empty;

    /// <summary>
    /// The raw <c>IssuerSigned</c> CBOR, <b>exactly as issued</b>. <b>mdoc only</b> — null for an SD-JWT VC.
    /// </summary>
    /// <remarks>
    /// Stored verbatim, and it must stay that way. Selective disclosure splices the <em>tag-24-wrapped item
    /// bytes</em> straight out of here; re-encoding an item can change bytes for the same logical value, which
    /// changes its digest, which invalidates the issuer's signature over data the issuer really did sign.
    /// </remarks>
    public byte[]? IssuerSignedCbor { get; init; }

    /// <summary>
    /// Names of every disclosable claim available in this credential. For an mdoc these are the element
    /// identifiers (e.g. <c>age_over_18</c>).
    /// </summary>
    public required IReadOnlyList<string> AvailableClaimNames { get; init; }

    /// <summary>Issuer DID — surfaced for the consent sheet.</summary>
    public string? IssuerDid { get; init; }

    /// <summary>Optional display label (issuer-supplied).</summary>
    public string? DisplayLabel { get; init; }

    /// <summary>
    /// The identifier this credential is matched on — <see cref="Vct"/> or <see cref="DocType"/> depending on
    /// <see cref="Format"/>.
    /// </summary>
    /// <remarks>
    /// A single accessor so no call site has to remember the rule, and so a new format cannot be added without
    /// deciding what it matches on.
    /// </remarks>
    public string MatchIdentifier => Format switch
    {
        CachedCredentialFormat.MsoMdoc => DocType ?? string.Empty,
        _ => Vct
    };
}

/// <summary>
/// Match result: a cached credential with the disclosure plan for the request.
/// A match exists only when every required claim is satisfiable.
/// </summary>
public sealed record CredentialMatch
{
    /// <summary>The matched credential.</summary>
    public required CachedCredential Credential { get; init; }

    /// <summary>Required claim names this credential can satisfy.</summary>
    public required IReadOnlyList<string> SatisfiedRequired { get; init; }

    /// <summary>Optional claim names this credential could additionally disclose.</summary>
    public required IReadOnlyList<string> AvailableOptional { get; init; }
}
