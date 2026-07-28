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

    /// <summary>
    /// Polls the verification result, distinguishing "this verifier has no such request" (a 404 —
    /// permanent, retrying cannot help) from a transient failure that may succeed next tick.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="GetVerificationResultAsync"/> in polling loops. The older
    /// method returns null for BOTH cases, which is what let the QR card poll a non-existent
    /// request for its full five-minute window and then report it as Expired.
    /// </remarks>
    Task<HaipPollOutcome> PollVerificationResultAsync(Guid requestId, CancellationToken ct = default);
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

/// <summary>
/// One poll of a verification result: the payload when the verifier answered, plus whether the
/// verifier reported that it holds no such request at all.
/// </summary>
/// <param name="Result">The verification result, or null when the poll did not yield one.</param>
/// <param name="RequestNotFound">
/// True only for a 404 — the verifier has no such request, so further polling is pointless.
/// False for transient failures, which are worth retrying.
/// </param>
public sealed record HaipPollOutcome(HaipVerificationResult? Result, bool RequestNotFound)
{
    /// <summary>The verifier has no such request (404).</summary>
    public static readonly HaipPollOutcome NotFound = new(null, RequestNotFound: true);

    /// <summary>A retryable failure — no result this tick, but the request may still exist.</summary>
    public static readonly HaipPollOutcome Transient = new(null, RequestNotFound: false);
}
