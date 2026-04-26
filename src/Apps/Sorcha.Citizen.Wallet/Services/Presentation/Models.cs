// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Citizen.Wallet.Services.Presentation;

/// <summary>Parsed shape of an <c>openid4vp://</c> cross-device deep link.</summary>
public sealed record ParsedPresentationRequest
{
    /// <summary>The verifier's audience claim — wallet's KB-JWT must echo this in <c>aud</c>.</summary>
    public required string ClientId { get; init; }

    /// <summary>URL the wallet POSTs the vp_token to (response_uri).</summary>
    public required string ResponseUri { get; init; }

    /// <summary>Verifier-supplied nonce — wallet's KB-JWT must echo it.</summary>
    public required string Nonce { get; init; }

    /// <summary>Required credential type URI from the presentation_definition.</summary>
    public required string RequiredVct { get; init; }

    /// <summary>Mandatory claim names the wallet must disclose.</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Optional claim names the wallet may disclose.</summary>
    public required IReadOnlyList<string> OptionalClaims { get; init; }

    /// <summary>Display purpose extracted from the presentation_definition.</summary>
    public string? Purpose { get; init; }

    /// <summary>Response mode; v1 supports <c>direct_post</c>.</summary>
    public string ResponseMode { get; init; } = "direct_post";
}

/// <summary>
/// A credential present in the wallet's cache, in the shape the presentation engine needs.
/// In production this is materialised by <c>ICredentialCache</c> on demand from IndexedDB.
/// </summary>
public sealed record CachedCredential
{
    /// <summary>Credential id (UUID assigned by the issuer).</summary>
    public required Guid Id { get; init; }

    /// <summary>Credential type URI.</summary>
    public required string Vct { get; init; }

    /// <summary>
    /// SD-JWT VC compact form: <c>credentialJwt~disclosure1~..~disclosureN</c>.
    /// Trailing tilde is preserved if present. Does NOT include a KB-JWT — that's
    /// minted at presentation time.
    /// </summary>
    public required string RawSdJwt { get; init; }

    /// <summary>Names of every disclosable claim available in this credential.</summary>
    public required IReadOnlyList<string> AvailableClaimNames { get; init; }

    /// <summary>Issuer DID — surfaced for the consent sheet.</summary>
    public string? IssuerDid { get; init; }

    /// <summary>Optional display label (issuer-supplied).</summary>
    public string? DisplayLabel { get; init; }
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
