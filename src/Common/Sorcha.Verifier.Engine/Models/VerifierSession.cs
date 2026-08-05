// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verification.Abstractions;

namespace Sorcha.Verifier.Engine.Models;

/// <summary>
/// In-memory verifier session — the link between a generated presentation request
/// (QR shown to the citizen) and the eventual outcome reported back to the
/// browser. One per QR scan attempt; pruned on completion or expiry.
/// </summary>
public sealed record VerifierSession
{
    /// <summary>Opaque session identifier embedded in the request URL.</summary>
    public required string SessionId { get; init; }

    /// <summary>Verifier organisation DID — the audience the wallet binds the KB-JWT to.</summary>
    public required string ClientId { get; init; }

    /// <summary>Random nonce — wallet must echo this in the KB-JWT.</summary>
    public required string Nonce { get; init; }

    /// <summary>The intended credential type URI (vct) the verifier requires.</summary>
    public required string RequiredVct { get; init; }

    /// <summary>Required claim names the citizen must disclose. Empty = none mandatory.</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Optional claim names the citizen may disclose. Used for consent UI.</summary>
    public IReadOnlyList<string> OptionalClaims { get; init; } = [];

    /// <summary>Human-readable purpose shown in the wallet's consent sheet.</summary>
    public required string Purpose { get; init; }

    /// <summary>UTC time the session was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC time the session expires (typically CreatedAt + 5 min).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Outcome — null while pending.</summary>
    public VerificationOutcome? Outcome { get; init; }
}

/// <summary>
/// Whether the credential's issuer signature was cryptographically verified (review H3).
/// Distinguishes a fully-verified credential from one accepted on the holder→device chain alone
/// because the issuer key could not be resolved and the verifier does not require it (offline /
/// reduced-assurance path).
/// </summary>
public enum IssuerSignatureStatus
{
    /// <summary>
    /// The issuer key could not be resolved and the verifier does not require the issuer signature
    /// (<c>requireIssuerSignature == false</c>); the credential was accepted on the holder→device chain
    /// and status list alone. The default — verifiers that require the issuer signature reject instead
    /// of reaching an accepted outcome with this value.
    /// </summary>
    NotVerified,

    /// <summary>The issuer key was resolved and the credential's issuer JWS verified against it.</summary>
    Verified,
}

/// <summary>
/// The four independent validation layers an open verifier surfaces for the verdict trail
/// (Feature 155). Each answers a distinct question; a credential can pass any subset.
/// </summary>
public enum ValidationLayer
{
    /// <summary>Is the live holder actually presenting it? (OID4VP + KB-JWT nonce/audience/freshness, delegation chain.)</summary>
    LivePresentation,

    /// <summary>Who signed it, and does the signature verify? (issuer DID resolution + JWS.)</summary>
    IssuerSignature,

    /// <summary>Is it still valid right now? (status-list revocation check.)</summary>
    Revocation,

    /// <summary>Was it genuinely recorded on the public register? (F079 Merkle inclusion proof.)</summary>
    RegisterAnchor,
}

/// <summary>
/// One step in the verdict's validation trail (Feature 155). Carries a short headline plus a
/// dictionary of raw key→value detail lines shown when the operator expands the step.
/// </summary>
public sealed record ValidationLayerResult
{
    /// <summary>Which validation layer this result describes.</summary>
    public required ValidationLayer Layer { get; init; }

    /// <summary>Verified / Failed / Unverified for this layer.</summary>
    public required VerificationStatus Status { get; init; }

    /// <summary>Short human-readable label, e.g. "Not revoked".</summary>
    public required string Headline { get; init; }

    /// <summary>Raw detail lines (key → value) revealed when the step is expanded. Never carries secrets.</summary>
    public IReadOnlyDictionary<string, string> Detail { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>Result of verifying a wallet's vp_token submission.</summary>
public sealed record VerificationOutcome
{
    /// <summary>True if every check passed.</summary>
    public required bool Accepted { get; init; }

    /// <summary>Disclosed claim values, keyed by claim name.</summary>
    public required IReadOnlyDictionary<string, object?> DisclosedClaims { get; init; }

    /// <summary>Human-readable rejection reasons. Empty when Accepted is true.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>UTC time the verifier completed validation.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Whether the credential's issuer signature was verified (review H3). Defaults to
    /// <see cref="IssuerSignatureStatus.NotVerified"/> so existing construction sites are unaffected;
    /// the validator sets <see cref="IssuerSignatureStatus.Verified"/> on the success path when the
    /// issuer JWS verified. For an accepted outcome under <c>requireIssuerSignature == true</c> this is
    /// always <see cref="IssuerSignatureStatus.Verified"/> (the unresolved-key path rejects instead).
    /// </summary>
    public IssuerSignatureStatus IssuerSignature { get; init; } = IssuerSignatureStatus.NotVerified;

    /// <summary>
    /// Per-layer validation results for the verdict trail (Feature 155). Defaults to empty so existing
    /// construction sites are unaffected. The validator populates the LivePresentation, IssuerSignature,
    /// and Revocation layers; the verifier app appends the RegisterAnchor layer after the anchor read
    /// (the engine performs no network I/O).
    /// </summary>
    public IReadOnlyList<ValidationLayerResult> Layers { get; init; } = [];
}
