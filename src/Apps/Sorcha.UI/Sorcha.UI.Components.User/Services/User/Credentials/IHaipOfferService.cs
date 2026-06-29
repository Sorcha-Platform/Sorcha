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
    /// Polls the Blueprint BFF for the current state of a presentation request.
    /// Returns a discriminated outcome distinguishing no-result-yet, transport errors, and terminal states.
    /// </summary>
    Task<VerificationPollOutcome> GetVerificationResultAsync(Guid requestId, CancellationToken ct = default);
}

/// <summary>
/// Discriminated outcome from a single <see cref="IHaipOfferService.GetVerificationResultAsync"/> poll.
/// Distinguishes three conditions: no result yet (keep polling), transport failure (error + retry),
/// and a terminal verification result.
/// </summary>
public record VerificationPollOutcome
{
    /// <summary>
    /// The verification result when a terminal state has been reached, or <c>null</c> when the session
    /// is still active (awaiting wallet scan or outcome processing). Populated only when
    /// <see cref="IsTransportError"/> is <c>false</c> and the BFF reports a terminal state.
    /// </summary>
    public HaipVerificationResult? Result { get; init; }

    /// <summary>
    /// <c>true</c> when the poll failed due to a transport error (auth rejection, server error,
    /// or network failure) that is distinct from an empty/pending result.
    /// </summary>
    public bool IsTransportError { get; init; }

    /// <summary>
    /// Human-readable error message populated when <see cref="IsTransportError"/> is <c>true</c>.
    /// </summary>
    public string? ErrorMessage { get; init; }
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
