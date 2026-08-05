// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Provenance.Engine.Seams;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine;

/// <summary>
/// Asks the five provenance questions of one sealed docket and returns the answers in order.
/// </summary>
/// <remarks>
/// <para>
/// Pure and deterministic: same evidence in, same trail out. It performs no IO, holds no state, and
/// cannot reach storage — the service assembles evidence and hands it over (plan D1).
/// </para>
/// <para>
/// <b>What this class does not do.</b> It performs no cryptographic verification, because it has no
/// cryptography available to it and acquiring some would foreclose the portable-export path it
/// exists for. In particular the <see cref="ProvenanceLayer.Signers"/> check <i>attributes</i> each
/// recorded vote to the validator set as it stood — it establishes that the vote claims a key that
/// held authority at that docket — rather than re-verifying signature bytes. That distinction is not
/// hidden from the reader: every check states its basis in
/// <see cref="ProvenanceCheck.CheckedAgainst"/>, and overstating it would breach FR-005 and make
/// this feature worse than none.
/// </para>
/// <para>
/// <b>The engine is never handed the current validator roster</b> — only
/// <see cref="RosterAsOf"/> (plan D5). See that type for why.
/// </para>
/// </remarks>
public sealed class DocketProvenanceVerifier
{
    private readonly IMerkleRootCalculator _merkle;

    /// <summary>
    /// Creates a verifier over the Merkle seam.
    /// </summary>
    /// <param name="merkleRootCalculator">
    /// Recomputes a docket's root using the same algorithm that sealed it. See
    /// <see cref="IMerkleRootCalculator"/> — implement by delegating, never by reimplementing.
    /// </param>
    public DocketProvenanceVerifier(IMerkleRootCalculator merkleRootCalculator)
    {
        _merkle = merkleRootCalculator ?? throw new ArgumentNullException(nameof(merkleRootCalculator));
    }

    /// <summary>
    /// Verifies one docket and returns one check per <see cref="ProvenanceLayer"/>, broadest first.
    /// </summary>
    /// <param name="registerId">The register the docket belongs to.</param>
    /// <param name="docket">The docket's stored evidence.</param>
    /// <param name="rosterAsOf">
    /// The validator set as it stood at this docket, or null when the applicable version could not
    /// be resolved. Never the current roster.
    /// </param>
    /// <param name="anchor">
    /// What this node knows about the register's origin and its own trust anchor, or null when
    /// nothing could be established.
    /// </param>
    /// <remarks>
    /// Never throws for missing evidence. Absent inputs become
    /// <see cref="VerificationStatus.Unverified"/> rows carrying reasons, so a view can render the
    /// parts it can establish and name the parts it cannot (FR-020, SC-009). Throwing would collapse
    /// a partially-answerable question into a blank page.
    /// </remarks>
    public ProvenanceTrail Verify(
        string registerId,
        DocketEvidence docket,
        RosterAsOf? rosterAsOf,
        AnchorEvidence? anchor)
    {
        ArgumentNullException.ThrowIfNull(docket);

        return new ProvenanceTrail
        {
            RegisterId = registerId,
            DocketNumber = docket.DocketNumber,
            Checks =
            [
                CheckAnchor(anchor),
                CheckChain(docket),
                CheckSeal(docket),
                CheckSigners(docket, rosterAsOf),
                CheckProposer(docket, rosterAsOf),
            ],
        };
    }

    private static ProvenanceCheck CheckAnchor(AnchorEvidence? anchor) => new()
    {
        Layer = ProvenanceLayer.Anchor,
        Status = VerificationStatus.Unverified,
        Headline = "Not implemented",
        CheckedAgainst = "nothing yet",
        Reason = "not implemented",
    };

    private static ProvenanceCheck CheckChain(DocketEvidence docket) => new()
    {
        Layer = ProvenanceLayer.Chain,
        Status = VerificationStatus.Unverified,
        Headline = "Not implemented",
        CheckedAgainst = "nothing yet",
        Reason = "not implemented",
    };

    private ProvenanceCheck CheckSeal(DocketEvidence docket) => new()
    {
        Layer = ProvenanceLayer.Seal,
        Status = VerificationStatus.Unverified,
        Headline = "Not implemented",
        CheckedAgainst = "nothing yet",
        Reason = "not implemented",
    };

    private static ProvenanceCheck CheckSigners(DocketEvidence docket, RosterAsOf? rosterAsOf) => new()
    {
        Layer = ProvenanceLayer.Signers,
        Status = VerificationStatus.Unverified,
        Headline = "Not implemented",
        CheckedAgainst = "nothing yet",
        Reason = "not implemented",
    };

    private static ProvenanceCheck CheckProposer(DocketEvidence docket, RosterAsOf? rosterAsOf) => new()
    {
        Layer = ProvenanceLayer.Proposer,
        Status = VerificationStatus.Unverified,
        Headline = "Not implemented",
        CheckedAgainst = "nothing yet",
        Reason = "not implemented",
    };
}
