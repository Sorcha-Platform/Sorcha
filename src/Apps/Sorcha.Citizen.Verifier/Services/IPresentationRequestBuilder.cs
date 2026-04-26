// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Citizen.Verifier.Services.Models;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Builds OID4VP cross-device presentation requests for the reference verifier
/// (Feature 114, T088). Persists the resulting <see cref="VerifierSession"/> in the
/// session store and returns the deep link the verifier UI renders as a QR.
/// </summary>
public interface IPresentationRequestBuilder
{
    /// <summary>
    /// Create a fresh session and return the QR-encodable <c>openid4vp://</c> deep link.
    /// </summary>
    /// <param name="verifierOrgId">Verifier org id — used to build the <c>client_id</c> DID.</param>
    /// <param name="purpose">Human-readable purpose displayed by the wallet.</param>
    /// <param name="requiredVct">Required credential type URI.</param>
    /// <param name="requiredClaims">Mandatory claim names.</param>
    /// <param name="optionalClaims">Optional claim names.</param>
    /// <param name="responseBaseUri">Base URI of the verifier (e.g. <c>https://verify.sorcha.dev</c>) — used to build the <c>response_uri</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deep link plus the underlying session.</returns>
    Task<PresentationRequestResult> CreateAsync(
        Guid verifierOrgId,
        string purpose,
        string requiredVct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims,
        string responseBaseUri,
        CancellationToken ct = default);
}

/// <summary>Result of <see cref="IPresentationRequestBuilder.CreateAsync"/>.</summary>
public sealed record PresentationRequestResult(string DeepLink, VerifierSession Session);
