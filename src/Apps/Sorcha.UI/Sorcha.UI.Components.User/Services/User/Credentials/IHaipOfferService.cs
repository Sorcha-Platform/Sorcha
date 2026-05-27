// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Service interface for polling HAIP credential offer and presentation request status.
/// Used by QR card components to track external wallet interactions.
/// </summary>
public interface IHaipOfferService
{
    /// <summary>
    /// Gets the current status of a credential offer.
    /// </summary>
    Task<HaipOfferStatus?> GetOfferStatusAsync(Guid offerId, CancellationToken ct = default);

    /// <summary>
    /// Gets the verification result for a presentation request.
    /// </summary>
    Task<HaipVerificationResult?> GetVerificationResultAsync(Guid requestId, CancellationToken ct = default);
}

/// <summary>Status of a HAIP credential offer.</summary>
public record HaipOfferStatus(
    Guid OfferId,
    string CredentialType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Verification result for a HAIP presentation request.</summary>
public record HaipVerificationResult(
    Guid RequestId,
    string State,
    bool? IsValid,
    Dictionary<string, JsonElement>? VerifiedClaims,
    IReadOnlyList<string>? Errors);
