// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Storage.Presentations;

/// <summary>
/// Feature 127 — short-TTL Redis stash of disclosed claims, written
/// alongside the F111 <c>presentation-outcome</c> transaction for the
/// disclosed-claims endpoint to read. Avoids re-decrypting the register
/// record from the claims-fetch path; the register tx remains the legal
/// record, this is the operational signal.
/// </summary>
/// <remarks>
/// <para>Lifecycle: written by <c>PresentationLifecycleService.HandleOutcomeAsync</c>
/// on a successful outcome (claims kept in plaintext, scoped by TTL); read by
/// <c>GET /api/presentations/{id}/disclosed-claims</c> when the council page
/// presents a valid single-use <c>ClaimsFetchToken</c>. Both writes and reads
/// are keyed by the presentation request id.</para>
/// <para>TTL = remaining validity window. The stash exists only as long as
/// the council page has time to fetch; after expiry, the register record
/// remains as the authoritative source.</para>
/// </remarks>
public interface IDisclosedClaimsStore
{
    /// <summary>
    /// Store disclosed claims keyed by presentation request id.
    /// </summary>
    Task StoreAsync(
        Guid presentationRequestId,
        IReadOnlyDictionary<string, object> claims,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>
    /// Read disclosed claims. Returns null when unknown or expired. Reads do
    /// not consume the entry — the entry expires by TTL only.
    /// </summary>
    /// <remarks>
    /// Single-use enforcement lives on the <see cref="IClaimsFetchTokenStore"/>
    /// (one fetch per token, not one fetch per claims stash). This split lets
    /// the council page survive a redeliver mid-flow without losing claims if
    /// the wallet posts twice (idempotent) — the token controls who can read,
    /// the stash controls what they read.
    /// </remarks>
    Task<IReadOnlyDictionary<string, object>?> GetAsync(
        Guid presentationRequestId,
        CancellationToken ct = default);
}
