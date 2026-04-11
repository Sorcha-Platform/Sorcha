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
    Task<CreatePresentationRequestResult> CreatePresentationRequestAsync(
        string credentialType,
        List<string>? requiredClaims = null,
        List<string>? acceptedIssuers = null,
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
public record CreateOfferResult(
    Guid OfferId,
    string CredentialOfferUri,
    string PreAuthorizedCode,
    DateTimeOffset ExpiresAt);

/// <summary>Result of creating a presentation request.</summary>
public record CreatePresentationRequestResult(
    Guid RequestId,
    string AuthorizationRequestUri,
    string RequestUri,
    string Nonce,
    DateTimeOffset ExpiresAt);

/// <summary>Status of a credential offer.</summary>
public record OfferStatusResult(
    Guid OfferId,
    string CredentialType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Verification result for a presentation request.</summary>
public record VerificationResultResponse(
    Guid RequestId,
    string State,
    bool? IsValid,
    Dictionary<string, object>? VerifiedClaims,
    List<string>? Errors);
