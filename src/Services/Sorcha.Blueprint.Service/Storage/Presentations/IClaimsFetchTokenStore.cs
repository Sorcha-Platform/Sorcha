// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Feature 127 — short-TTL, single-use token store bound to a
/// presentation request. Used by the council page to authenticate against
/// <c>GET /api/presentations/{id}/disclosed-claims</c> for autofill.
/// </summary>
/// <remarks>
/// <para>The council page is unauthenticated in the broader sense (no user
/// cookie, no bearer token). The token-based scheme provides the auth scope:
/// F111's <c>InitiateAsync</c> mints a fresh token alongside the
/// <c>presentationRequestId</c> and returns it ONLY to the originator of the
/// presentation request. The council page presents this token on the
/// claims-fetch endpoint. The token is consumed atomically on first use
/// (NonceStore pattern — single-use enforced).</para>
/// <para>Bound state: the value at the token key is the <c>presentationRequestId</c>
/// the token authorises. The endpoint compares this against the path
/// parameter to reject token/requestId mismatch.</para>
/// </remarks>
public interface IClaimsFetchTokenStore
{
    /// <summary>
    /// Store a freshly-minted token bound to a presentation request.
    /// </summary>
    /// <param name="token">High-entropy URL-safe value. Must be unguessable.</param>
    /// <param name="presentationRequestId">The presentation request the token authorises.</param>
    /// <param name="ttl">Token validity window. Typically the remaining time until presentation expiry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreAsync(
        string token,
        Guid presentationRequestId,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically read the bound presentationRequestId and delete the token in
    /// one operation. Returns null if the token is unknown, expired, or already
    /// consumed.
    /// </summary>
    /// <remarks>
    /// First caller wins. Subsequent calls with the same token return null.
    /// </remarks>
    Task<Guid?> GetAndRemoveAsync(string token, CancellationToken ct = default);
}
