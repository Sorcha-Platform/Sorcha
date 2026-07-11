// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Haip;

/// <summary>
/// Client interface for the Sorcha HAIP Service (specs 097/098).
/// Used by the Blueprint Service to create credential offers and
/// presentation requests for external HAIP wallets.
/// </summary>
public interface IHaipServiceClient
{
    /// <summary>
    /// Creates a Credential Offer for issuance to an external HAIP wallet.
    /// Returns the offer ID and an openid-credential-offer:// URI for QR rendering.
    /// </summary>
    Task<CreateOfferResult> CreateCredentialOfferAsync(
        string issuerWalletAddress,
        string tenantId,
        string credentialType,
        Dictionary<string, object> claims,
        List<string>? disclosablePaths = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a Presentation Request for verification from an external HAIP wallet.
    /// Returns the request ID and an openid4vp://authorize URI for QR rendering.
    /// </summary>
    /// <param name="declaredQueryJson">Feature 181 US2 — optional pre-built DCQL query
    /// (serialized <c>dcql_query</c> JSON) covering every credential ask on the action,
    /// including <c>credential_sets</c> alternatives. When supplied, the HAIP verifier serves
    /// it verbatim as the request object's <c>dcql_query</c>; null ⇒ the verifier builds a
    /// single-ask query from <paramref name="credentialType"/> + <paramref name="requiredClaims"/>.
    /// Passed as JSON so this client stays free of a DCQL-model dependency.</param>
    Task<CreatePresentationRequestResult> CreatePresentationRequestAsync(
        string credentialType,
        List<string>? requiredClaims = null,
        List<string>? acceptedIssuers = null,
        string? declaredQueryJson = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a credential offer.
    /// </summary>
    Task<OfferStatusResult?> GetOfferStatusAsync(Guid offerId, CancellationToken ct = default);

    /// <summary>
    /// Gets the verification result for a presentation request.
    /// </summary>
    Task<VerificationResultResponse?> GetVerificationResultAsync(Guid requestId, CancellationToken ct = default);
}

/// <summary>Result of creating a credential offer.</summary>
/// <param name="OfferId">Identifier of the offer.</param>
/// <param name="CredentialOfferUri">The credential offer uri.</param>
/// <param name="PreAuthorizedCode">The pre authorized code.</param>
/// <param name="ExpiresAt">Timestamp at which the record expires (UTC).</param>
public record CreateOfferResult(
    Guid OfferId,
    string CredentialOfferUri,
    string PreAuthorizedCode,
    DateTimeOffset ExpiresAt);

/// <summary>Result of creating a presentation request.</summary>
/// <param name="RequestId">Identifier of this request.</param>
/// <param name="AuthorizationRequestUri">The authorization request uri.</param>
/// <param name="RequestUri">The request uri.</param>
/// <param name="Nonce">The nonce.</param>
/// <param name="ExpiresAt">Timestamp at which the record expires (UTC).</param>
public record CreatePresentationRequestResult(
    Guid RequestId,
    string AuthorizationRequestUri,
    string RequestUri,
    string Nonce,
    DateTimeOffset ExpiresAt);

/// <summary>Status of a credential offer.</summary>
/// <param name="OfferId">Identifier of the offer.</param>
/// <param name="CredentialType">The credential type.</param>
/// <param name="Status">Current status of the resource.</param>
/// <param name="CreatedAt">Server timestamp when the record was created (UTC).</param>
/// <param name="ExpiresAt">Timestamp at which the record expires (UTC).</param>
public record OfferStatusResult(
    Guid OfferId,
    string CredentialType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Verification result for a presentation request.</summary>
/// <param name="RequestId">Identifier of this request.</param>
/// <param name="State">Current state of the resource.</param>
/// <param name="IsValid">Indicates whether validation passed.</param>
/// <param name="VerifiedClaims">Map of verified claims keyed by string.</param>
/// <param name="Errors">Collection of error details when the operation did not succeed.</param>
public record VerificationResultResponse(
    Guid RequestId,
    string State,
    bool? IsValid,
    Dictionary<string, object>? VerifiedClaims,
    List<string>? Errors);
