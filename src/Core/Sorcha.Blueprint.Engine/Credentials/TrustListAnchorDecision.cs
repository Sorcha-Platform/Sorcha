// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The single implementation of the trusted-list anchor decision (Feature 181 US3): given a
/// resolved snapshot's facts, decide whether to vouch and, if so, with what evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is centralised.</b> Blueprint Service and HAIP each own a thin
/// <c>ITenantTrustAnchorProvider</c> adapter, because each fetches snapshots through its own
/// configured client. Those two adapters were byte-identical apart from their comments — including
/// the two security-relevant rules below. Two copies of a fail-closed rule is one copy that can be
/// fixed and one that cannot:
/// </para>
/// <list type="bullet">
///   <item><b>FR-014</b> — an absent or empty snapshot yields no anchors, so the trust source fails closed.</item>
///   <item><b>FR-016</b> — a stale snapshot fails closed under strict freshness, and vouches with a
///   stale-flagged evidence trail under the default warn mode. Either way the evaluation is metered.</item>
/// </list>
/// <para>
/// It takes primitives rather than a snapshot type because the snapshot lives in
/// <c>Sorcha.ServiceClients.Http</c> (a <c>src/Common</c> assembly) while <see cref="TrustAnchorSet"/>
/// and <see cref="TrustMetrics"/> live here in <c>src/Core</c>. No <c>src/Common</c> project
/// references <c>src/Core</c>, and the engine is deliberately free of network-bound providers — so
/// neither assembly can host the adapter itself. Passing facts across the seam keeps both invariants
/// intact and still leaves exactly one copy of the decision.
/// </para>
/// </remarks>
public static class TrustListAnchorDecision
{
    /// <summary>
    /// Evaluates a resolved trusted-list snapshot into a <see cref="TrustAnchorSet"/>, or
    /// <see langword="null"/> to fail closed.
    /// </summary>
    /// <param name="roots">DER-encoded trusted roots extracted from the snapshot. Empty fails closed (FR-014).</param>
    /// <param name="snapshotId">The trusted-list identifier, e.g. <c>eu-lotl</c>.</param>
    /// <param name="sequenceNumber">The snapshot's TS 119 612 sequence number.</param>
    /// <param name="freshness">The snapshot's freshness timestamp, carried into the evidence trail.</param>
    /// <param name="isStale">Whether the snapshot is past its effective next-update.</param>
    /// <param name="strictFreshness">When true, a stale snapshot fails closed (FR-016).</param>
    /// <param name="logger">Optional logger for the operator-facing stale warning.</param>
    /// <returns>The anchor set to vouch with, or <see langword="null"/> to supply no anchors.</returns>
    public static TrustAnchorSet? Evaluate(
        IReadOnlyList<byte[]>? roots,
        string snapshotId,
        long sequenceNumber,
        DateTimeOffset? freshness,
        bool isStale,
        bool strictFreshness,
        ILogger? logger = null)
    {
        // FR-014 — absent or empty snapshot: supply nothing, so the source fails closed.
        if (roots is null || roots.Count == 0)
        {
            return null;
        }

        // FR-016 — freshness gate. Warn mode (default) flags and still vouches; strict fails closed.
        if (isStale)
        {
            TrustMetrics.RecordStaleEvaluation(snapshotId, sequenceNumber, strictFreshness);

            if (strictFreshness)
            {
                logger?.LogWarning(
                    "Trusted-list snapshot {TrustListId}#{Sequence} is stale and strict freshness is enabled — failing closed (TRUSTLIST_STALE).",
                    snapshotId, sequenceNumber);
                return null;
            }

            logger?.LogWarning(
                "Trusted-list snapshot {TrustListId}#{Sequence} is stale (warn mode) — vouching with a stale-flagged evidence trail.",
                snapshotId, sequenceNumber);
        }

        return new TrustAnchorSet
        {
            Roots = roots,
            // FR-015 — the snapshot identity that flows into TrustEvidence.TrustListId. The exact
            // format is part of the evidence contract, so it is composed in one place only.
            AnchorSetId = $"{snapshotId}#{sequenceNumber}",
            Freshness = freshness,
            CheckRevocation = false,
        };
    }
}
