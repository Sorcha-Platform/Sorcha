// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Service for creating, verifying, and presenting SD-JWT VC tokens
/// per RFC 9901 (SD-JWT) and the SD-JWT VC profile.
/// </summary>
public interface ISdJwtService
{
    /// <summary>
    /// Creates a new SD-JWT VC token with selective disclosure support.
    /// </summary>
    /// <param name="claims">All credential claims to include.</param>
    /// <param name="disclosableClaims">Claim names that support selective disclosure. Null = all disclosable.</param>
    /// <param name="issuer">Issuer identifier (DID URI or wallet address).</param>
    /// <param name="subject">Subject identifier (DID URI or wallet address).</param>
    /// <param name="signingKey">Private key bytes for signing (algorithm determined by key type).</param>
    /// <param name="algorithm">Signing algorithm (e.g., "EdDSA", "ES256", "RS256").</param>
    /// <param name="expiresAt">Optional expiration timestamp.</param>
    /// <param name="x5cChain">
    /// Feature 096 US3 — optional X.509 certificate chain (leaf first) to embed
    /// in the JWS header's <c>x5c</c> array per RFC 7515 §4.1.6. When supplied,
    /// verifiers can validate the issuer key against a trust anchor without DID
    /// resolution. Each entry is raw DER bytes; the header encodes them as
    /// base64 (not base64url).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created SD-JWT token with all disclosures.</returns>
    Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null,
        string? kid = null);

    /// <summary>
    /// Creates a new SD-JWT VC token with selective disclosure and holder key binding (cnf).
    /// </summary>
    /// <param name="claims">All credential claims to include.</param>
    /// <param name="disclosableClaims">Claim names that support selective disclosure. Null = all disclosable.</param>
    /// <param name="issuer">Issuer identifier (DID URI or wallet address).</param>
    /// <param name="subject">Subject identifier (DID URI or wallet address).</param>
    /// <param name="signingKey">Private key bytes for signing.</param>
    /// <param name="algorithm">Signing algorithm (e.g., "EdDSA", "ES256", "RS256").</param>
    /// <param name="holderJwk">Holder's public key in JWK form, embedded as the <c>cnf.jwk</c> claim.</param>
    /// <param name="expiresAt">Optional expiration timestamp.</param>
    /// <param name="x5cChain">Feature 096 US3 — optional X.509 chain for the JWS header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created SD-JWT token with cnf claim and all disclosures.</returns>
    Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        byte[] signingKey,
        string algorithm,
        JsonElement holderJwk,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null,
        string? kid = null);

    /// <summary>
    /// External-signer overload for sign-on-behalf flows (Feature 120 HAIP kid-swap).
    /// The caller supplies an <paramref name="externalSigner"/> that produces the
    /// signature for the unsigned JWS bytes; this overload carries no signing key
    /// (unlike the key-based overload) — the external signer holds it. Used by HAIP service to
    /// delegate signing to wallet without holding private key material.
    /// </summary>
    Task<SdJwtToken> CreateTokenAsync(
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosableClaims,
        string issuer,
        string subject,
        string algorithm,
        Func<byte[], CancellationToken, Task<byte[]>> externalSigner,
        JsonElement? holderJwk,
        string? kid,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<byte[]>? x5cChain = null);

    /// <summary>
    /// Verifies an SD-JWT token's signature, structure, and extracts all disclosed claims.
    /// </summary>
    /// <param name="rawToken">The serialized SD-JWT token.</param>
    /// <param name="issuerPublicKey">Issuer's public key for signature verification.</param>
    /// <param name="algorithm">Expected signing algorithm.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="issuerRecoveryAddress">
    /// Feature 178 — when the issuer is an address-form DID (<c>did:pkh</c> / address-form
    /// <c>did:ethr</c>) that publishes no key, the CAIP-10 / <c>0x…</c> address to verify the ES256K
    /// issuer signature against by public-key recovery. When set, <paramref name="issuerPublicKey"/>
    /// is empty and <paramref name="algorithm"/> is <c>"ES256K"</c>. Null for key-bearing issuers.
    /// </param>
    /// <returns>Verification result with extracted claims.</returns>
    Task<SdJwtVerificationResult> VerifyTokenAsync(
        string rawToken,
        byte[] issuerPublicKey,
        string algorithm,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null);

    /// <summary>
    /// Creates a presentation from an SD-JWT token, disclosing only selected claims.
    /// </summary>
    /// <param name="rawToken">The complete SD-JWT token.</param>
    /// <param name="claimsToDisclose">Claim names to reveal in the presentation.</param>
    /// <param name="holderKey">Holder's private key for key binding proof (optional).</param>
    /// <param name="audience">Intended verifier (for key binding JWT).</param>
    /// <param name="nonce">Nonce for key binding JWT replay prevention.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SD-JWT presentation with selected disclosures.</returns>
    Task<SdJwtPresentation> CreatePresentationAsync(
        string rawToken,
        IEnumerable<string> claimsToDisclose,
        byte[]? holderKey = null,
        string? audience = null,
        string? nonce = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a presentation with a Key Binding JWT signed by a delegate.
    /// The delegate receives the signing input bytes and returns the signature.
    /// This allows the Wallet Service to sign without exposing the private key.
    /// </summary>
    /// <param name="rawToken">The complete SD-JWT token.</param>
    /// <param name="claimsToDisclose">Claim names to reveal in the presentation.</param>
    /// <param name="kbJwtSigner">Delegate that signs the KB-JWT input bytes and returns the signature.</param>
    /// <param name="holderAlgorithm">Algorithm for the KB-JWT header (e.g., "ES256", "EdDSA").</param>
    /// <param name="audience">Verifier audience URI for the KB-JWT <c>aud</c> claim.</param>
    /// <param name="nonce">Verifier nonce for the KB-JWT <c>nonce</c> claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SD-JWT presentation with KB-JWT appended.</returns>
    Task<SdJwtPresentation> CreatePresentationAsync(
        string rawToken,
        IEnumerable<string> claimsToDisclose,
        Func<byte[], CancellationToken, Task<byte[]>> kbJwtSigner,
        string holderAlgorithm,
        string audience,
        string nonce,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an SD-JWT presentation, extracting only the disclosed claims.
    /// </summary>
    /// <param name="rawPresentation">The serialized SD-JWT presentation.</param>
    /// <param name="issuerPublicKey">Issuer's public key for signature verification.</param>
    /// <param name="algorithm">Expected signing algorithm.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result with only disclosed claims.</returns>
    Task<SdJwtVerificationResult> VerifyPresentationAsync(
        string rawPresentation,
        byte[] issuerPublicKey,
        string algorithm,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null);

    /// <summary>
    /// Verifies an SD-JWT presentation including KB-JWT validation against the
    /// holder's confirmation key from the credential's <c>cnf</c> claim.
    /// </summary>
    /// <param name="rawPresentation">The serialized SD-JWT presentation (ending with KB-JWT).</param>
    /// <param name="issuerPublicKey">Issuer's public key for signature verification.</param>
    /// <param name="algorithm">Expected issuer signing algorithm.</param>
    /// <param name="expectedAudience">Expected <c>aud</c> in the KB-JWT.</param>
    /// <param name="expectedNonce">Expected <c>nonce</c> in the KB-JWT.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result with disclosed claims and holder key verification status.</returns>
    Task<SdJwtVerificationResult> VerifyPresentationAsync(
        string rawPresentation,
        byte[] issuerPublicKey,
        string algorithm,
        string expectedAudience,
        string expectedNonce,
        CancellationToken cancellationToken = default,
        string? issuerRecoveryAddress = null);
}
