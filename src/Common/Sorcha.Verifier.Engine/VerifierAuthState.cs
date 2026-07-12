// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Feature 181 US6 (data-model §5, FR-027) — the three-state verifier-authentication verdict a wallet
/// renders on its consent surface after validating a signed request object. A request is NEVER presented
/// as verified unless its signature checks out and its certificate chains to a trusted-list anchor.
/// </summary>
public enum VerifierAuthStatus
{
    /// <summary>Signature valid, SAN matches the client_id host, and the cert chains to a trusted-list anchor.</summary>
    TrustedListVerified,

    /// <summary>Signature valid and SAN matches, but the cert is not on any available trusted list
    /// (or no anchors are available — which never blocks, FR-027).</summary>
    AuthenticUntrusted,

    /// <summary>Authenticity could not be established (no x5c, unsupported alg, unparseable cert, or the
    /// client_id is not an <c>x509_san_dns:</c> identity). Rendered as "unverifiable" — never as verified.</summary>
    Unverifiable,
}

/// <summary>
/// Feature 181 US6 — the verifier-authentication state carried to the consent surface.
/// </summary>
/// <param name="Status">The three-state verdict.</param>
/// <param name="VerifierHost">The verifier's DNS host (from the cert SAN / client_id), when known.</param>
/// <param name="AnchorSetId">The trusted-list snapshot identity the cert chained to (only when <see cref="VerifierAuthStatus.TrustedListVerified"/>).</param>
/// <param name="Reason">A citizen-comprehensible reason (only when <see cref="VerifierAuthStatus.Unverifiable"/>).</param>
public sealed record VerifierAuthState(
    VerifierAuthStatus Status,
    string? VerifierHost = null,
    string? AnchorSetId = null,
    string? Reason = null)
{
    /// <summary>Convenience — true only for <see cref="VerifierAuthStatus.TrustedListVerified"/>.</summary>
    public bool IsTrusted => Status == VerifierAuthStatus.TrustedListVerified;
}

/// <summary>
/// Feature 181 US6 — the outcome of validating a signed request object. Either a HARD refusal (the request
/// MUST NOT proceed — a present-but-invalid signature, or a SAN that doesn't match the client_id host), or a
/// three-state <see cref="VerifierAuthState"/> the consent surface renders.
/// </summary>
/// <param name="Refused">True when the request must be refused outright.</param>
/// <param name="RefusalCode"><see cref="RequestObjectErrorCodes.RequestObjectInvalid"/> or <see cref="RequestObjectErrorCodes.RequestHostMismatch"/>.</param>
/// <param name="AuthState">The rendered verifier-auth state (null when <paramref name="Refused"/>).</param>
public sealed record RequestObjectValidationResult(bool Refused, string? RefusalCode, VerifierAuthState? AuthState)
{
    /// <summary>A hard refusal with the given code.</summary>
    public static RequestObjectValidationResult Refuse(string code) => new(true, code, null);

    /// <summary>A rendered three-state verdict.</summary>
    public static RequestObjectValidationResult Render(VerifierAuthState state) => new(false, null, state);
}

/// <summary>Feature 181 US6 — typed request-object refusal codes (data-model §6, FR-026).</summary>
public static class RequestObjectErrorCodes
{
    /// <summary>Malformed request object, or a present signature that does not verify (tampered).</summary>
    public const string RequestObjectInvalid = "REQUEST_OBJECT_INVALID";

    /// <summary>The certificate SAN dNSName does not match the <c>x509_san_dns:</c> client_id host.</summary>
    public const string RequestHostMismatch = "REQUEST_HOST_MISMATCH";
}

/// <summary>
/// Feature 181 US6 — a set of trusted-list CA anchors the wallet supplies for the chain-to-anchor check.
/// Engine-local (no dependency on the F135 <c>TrustAnchorSet</c>); the PWA maps its fetched anchors onto it.
/// </summary>
/// <param name="Roots">DER-encoded trusted CA root certificates.</param>
/// <param name="AnchorSetId">Snapshot identity (<c>{trustListId}#{seq}</c>) carried into the verdict.</param>
public sealed record VerifierTrustAnchors(IReadOnlyList<byte[]> Roots, string? AnchorSetId);
