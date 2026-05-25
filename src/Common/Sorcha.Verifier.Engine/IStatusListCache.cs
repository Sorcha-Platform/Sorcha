// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verifier.Engine;

/// <summary>
/// The verdict of a status-list revocation check (Feature 138 US1). A tri-state so the caller
/// can <b>fail closed</b>: anything other than <see cref="Active"/> must block verification.
/// </summary>
public enum StatusListVerdict
{
    /// <summary>The list was authenticated and fresh, and the credential's bit is clear (not revoked).</summary>
    Active = 0,

    /// <summary>The list was authenticated and fresh, and the credential's bit is set (revoked).</summary>
    Revoked = 1,

    /// <summary>
    /// The list could not be authenticated against sealed-state-anchored trust — bad/absent signature,
    /// issuer mismatch, unresolved key, expired list, or fetch failure. The caller MUST treat this as a
    /// verification failure (fail closed), never as "active".
    /// </summary>
    Unverifiable = 2,
}

/// <summary>
/// Verifier-side cache of signed Token Status List 2024 JWTs (Feature 114; hardened in Feature 138 US1).
/// Used by the VP validator to check whether a delegation credential — or any other status-list-tracked
/// artefact — has been revoked.
/// </summary>
/// <remarks>
/// <para>
/// Feature 138 closes the revocation-forgery gap: the cache now verifies the status-list JWT signature
/// against the issuing organisation's key resolved from sealed register state
/// (<see cref="IIssuerKeyResolver"/>), pins the list's <c>iss</c> claim to the expected org DID, enforces
/// freshness against the list's own <c>exp</c> (within a bounded clock skew), and <b>fails closed</b> on
/// any inability to verify. A fetch failure no longer serves a stale cached copy; only a fully-verified
/// list is ever cached.
/// </para>
/// </remarks>
public interface IStatusListCache
{
    /// <summary>
    /// Authenticates the status list at <paramref name="statusListUri"/> against
    /// <paramref name="expectedIssuer"/> and returns the verdict for the bit at <paramref name="index"/>.
    /// </summary>
    /// <param name="statusListUri">Public URI of the Token Status List JWT.</param>
    /// <param name="index">Bit position to evaluate.</param>
    /// <param name="expectedIssuer">
    /// The org DID the list MUST be issued by (the consuming credential's <c>iss</c>, e.g.
    /// <c>did:sorcha:org:{orgId:N}</c>). A list whose <c>iss</c> differs is <see cref="StatusListVerdict.Unverifiable"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="StatusListVerdict.Active"/>, <see cref="StatusListVerdict.Revoked"/>, or
    /// <see cref="StatusListVerdict.Unverifiable"/> (fail closed).
    /// </returns>
    Task<StatusListVerdict> CheckAsync(
        string statusListUri, int index, string expectedIssuer, CancellationToken ct = default);

    /// <summary>
    /// Forces a fresh, verified fetch of the given status list, replacing any cached entry. The entry is
    /// replaced only if the fetched list authenticates against <paramref name="expectedIssuer"/>.
    /// </summary>
    Task RefreshAsync(string statusListUri, string expectedIssuer, CancellationToken ct = default);
}
