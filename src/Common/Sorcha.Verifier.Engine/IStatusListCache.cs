// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Verifier-side cache of signed Token Status List 2024 JWTs (Feature 114, T072).
/// Used by the VP validator to check whether a delegation credential — or any
/// other status-list-tracked artefact — has been revoked.
/// </summary>
/// <remarks>
/// Verifiers MUST tolerate a stale cache up to the list's <c>exp</c> per the spec
/// — that's the point of the publisher's signed <c>exp</c> claim. Beyond <c>exp</c>
/// the cached entry is considered authoritative-but-expired and a fresh fetch is
/// attempted; on a network failure during a fresh fetch the verifier falls back
/// to the stale entry with a logged warning rather than rejecting the
/// presentation outright (offline verifier scenario).
/// </remarks>
public interface IStatusListCache
{
    /// <summary>
    /// Returns true if the bit at <paramref name="index"/> in the status list at
    /// <paramref name="statusListUri"/> is set (revoked).
    /// </summary>
    /// <param name="statusListUri">Public URI of the Token Status List JWT.</param>
    /// <param name="index">Bit position to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if revoked, false if active or unable to determine.</returns>
    Task<bool> IsRevokedAsync(string statusListUri, int index, CancellationToken ct = default);

    /// <summary>
    /// Forces a fresh fetch of the given status list, replacing any cached entry.
    /// </summary>
    Task RefreshAsync(string statusListUri, CancellationToken ct = default);
}
