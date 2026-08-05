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

    /// <summary>
    /// Does the register's origin trace to the trust anchor this node holds?
    /// </summary>
    /// <remarks>
    /// Three distinct not-knowing cases all resolve to
    /// <see cref="VerificationStatus.Unverified"/>, never
    /// <see cref="VerificationStatus.Failed"/>: no evidence assembled at all, a node holding no
    /// anchor, and a register whose origin the anchor does not cover. Only a fingerprint that is
    /// present on both sides and differs is a failure — that is a genuine contradiction between two
    /// known values, and the only case where "this did not come from where it claims" is a
    /// statement about the register rather than about our own configuration.
    /// </remarks>
    private static ProvenanceCheck CheckAnchor(AnchorEvidence? anchor)
    {
        const string against =
            "the trust anchor this node holds, compared with the fingerprint recorded for the register's origin";

        if (anchor is null)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Anchor,
                Status = VerificationStatus.Unverified,
                Headline = "Origin could not be established",
                CheckedAgainst = against,
                Reason = "no anchor evidence could be assembled for this register on this node",
            };
        }

        if (!anchor.IsAnchorKnown)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Anchor,
                Status = VerificationStatus.Unverified,
                Headline = "This node holds no trust anchor",
                CheckedAgainst = against,
                Reason =
                    "this node does not hold a trust anchor, so it cannot say what a register's origin " +
                    "ought to trace to (issue #1374)",
            };
        }

        if (string.IsNullOrWhiteSpace(anchor.OriginFingerprint) ||
            string.IsNullOrWhiteSpace(anchor.AnchorFingerprint))
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Anchor,
                Status = VerificationStatus.Unverified,
                Headline = "Origin does not trace to this node's trust anchor",
                CheckedAgainst = against,
                Detail = Describe(anchor),
                Reason = anchor.UntraceableReason
                         ?? "no origin fingerprint is recorded for this register, so there is nothing to compare",
            };
        }

        // Hex fingerprints: a casing difference is not a mismatch, and reporting one as tampering
        // would be a false accusation produced by an encoding detail.
        var traces = string.Equals(
            anchor.OriginFingerprint,
            anchor.AnchorFingerprint,
            StringComparison.OrdinalIgnoreCase);

        return new ProvenanceCheck
        {
            Layer = ProvenanceLayer.Anchor,
            Status = traces ? VerificationStatus.Verified : VerificationStatus.Failed,
            Headline = traces
                ? "Origin traces to this node's trust anchor"
                : "Origin does NOT match this node's trust anchor",
            CheckedAgainst = against,
            Detail = Describe(anchor),
        };
    }

    private static string Describe(AnchorEvidence anchor)
    {
        var origin = string.IsNullOrWhiteSpace(anchor.OriginFingerprint) ? "(none recorded)" : anchor.OriginFingerprint;
        var held = string.IsNullOrWhiteSpace(anchor.AnchorFingerprint) ? "(none held)" : anchor.AnchorFingerprint;
        var what = string.IsNullOrWhiteSpace(anchor.OriginDescription) ? "the register's origin record" : anchor.OriginDescription;

        return $"Origin ({what}): {origin} · trust anchor held by this node: {held}";
    }

    /// <summary>
    /// Does this docket link to the one before it?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not holding the predecessor is <see cref="VerificationStatus.Unverified"/>, and the reason
    /// names the missing docket.</b> A Sorcha node routinely holds part of a register — a subscriber
    /// syncs what it needs — so treating absence as failure would render every subscribing node in
    /// the network as compromised. Naming the docket number is what makes the result actionable:
    /// "which link could not be established" is the whole question (SC-009).
    /// </para>
    /// <para>
    /// A docket that claims no predecessor while this node holds one IS a failure. That is a
    /// contradiction between two things the node can see, not an absence.
    /// </para>
    /// </remarks>
    private static ProvenanceCheck CheckChain(DocketEvidence docket)
    {
        const string against = "the hash of the predecessor docket as held by this node";

        var claimed = docket.ClaimedPreviousHash;
        var actual = docket.PredecessorHash;

        if (string.IsNullOrWhiteSpace(actual))
        {
            var reason = docket.DocketNumber == 0
                ? "this is the genesis docket — it has no predecessor to link to"
                : $"this node does not hold docket {docket.DocketNumber - 1}, so there is nothing to compare this docket's link against";

            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Chain,
                Status = VerificationStatus.Unverified,
                Headline = docket.DocketNumber == 0
                    ? "Genesis docket — no predecessor"
                    : "Predecessor not held by this node",
                CheckedAgainst = against,
                Detail = $"Claimed predecessor: {Show(claimed)} · held by this node: (none)",
                Reason = reason,
            };
        }

        var links = string.Equals(claimed, actual, StringComparison.OrdinalIgnoreCase);

        return new ProvenanceCheck
        {
            Layer = ProvenanceLayer.Chain,
            Status = links ? VerificationStatus.Verified : VerificationStatus.Failed,
            Headline = links
                ? $"Links to docket {docket.DocketNumber - 1}"
                : $"Does NOT link to docket {docket.DocketNumber - 1}",
            CheckedAgainst = against,
            Detail = $"Claimed predecessor: {Show(claimed)} · held by this node: {Show(actual)}",
        };
    }

    private static string Show(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none recorded)" : value;

    /// <summary>
    /// Do the docket's recorded contents still match the commitment sealed over them?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this establishes, exactly.</b> The root is recomputed from the docket's stored
    /// transaction ids, in stored order, using the same algorithm that sealed it (via
    /// <see cref="IMerkleRootCalculator"/>), and compared with the root the proposing validator
    /// committed. Agreement proves the stored transaction list is unchanged <i>since sealing</i>. It
    /// does not prove the algorithm is correct, and it does not independently validate the
    /// transactions themselves. FR-005 requires the check to say so rather than imply otherwise, so
    /// <see cref="ProvenanceCheck.CheckedAgainst"/> states the basis in those terms — and a test
    /// asserts on that wording, because an overstated claim here is the difference between evidence
    /// and manufactured confidence.
    /// </para>
    /// <para>
    /// <b>Order is not normalised.</b> The commitment covers the sequence, so the ids are passed
    /// through as stored. Sorting before comparing would let a reordering pass.
    /// </para>
    /// <para>
    /// A docket sealed before Feature 187 persisted the commitment has nothing to compare against
    /// and reports <see cref="VerificationStatus.Unverified"/>. Blank is treated exactly as absent:
    /// the register stores <c>MerkleRoot</c> as a non-nullable string defaulting to empty, so that is
    /// the form a real pre-Feature-187 docket actually arrives in.
    /// </para>
    /// </remarks>
    private ProvenanceCheck CheckSeal(DocketEvidence docket)
    {
        const string against =
            "the sealed Merkle root, compared with a root recomputed from the docket's stored transaction ids";

        if (string.IsNullOrWhiteSpace(docket.SealedMerkleRoot))
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Seal,
                Status = VerificationStatus.Unverified,
                Headline = "No sealed commitment was recorded for this docket",
                CheckedAgainst = against,
                Detail = $"Transactions sealed: {docket.TransactionIds.Count}",
                Reason =
                    "this docket was sealed before the platform kept the sealed Merkle root, so there " +
                    "is no commitment to compare a recomputation against",
            };
        }

        var recomputed = _merkle.ComputeRoot(docket.TransactionIds);
        var matches = string.Equals(recomputed, docket.SealedMerkleRoot, StringComparison.OrdinalIgnoreCase);

        return new ProvenanceCheck
        {
            Layer = ProvenanceLayer.Seal,
            Status = matches ? VerificationStatus.Verified : VerificationStatus.Failed,
            Headline = matches
                ? "Contents are unchanged since sealing"
                : "Contents do NOT match the sealed commitment",
            CheckedAgainst = against,
            Detail =
                $"Sealed root: {docket.SealedMerkleRoot} · recomputed: {recomputed} · " +
                $"over {docket.TransactionIds.Count} transaction id(s) in stored order",
        };
    }

    /// <summary>
    /// Did the validators who signed hold authority <b>at that point in history</b>?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The roster is the one that applied at this docket, and no other</b> (FR-010, plan D5).
    /// The method has no access to the current roster — not by convention, but because no parameter
    /// carries it. An implementation judging history against the present set passes every test that
    /// uses a register whose validator set never changed, and starts reporting false tampering the
    /// moment a validator is removed. See <c>RosterAsOfTests</c>.
    /// </para>
    /// <para>
    /// <b>What "valid against the roster" means here, precisely.</b> The engine performs no
    /// cryptography (plan D1), so each recorded vote is <i>attributed</i>: the validator it names
    /// must be present in the roster version applying at this docket, must have held authority then,
    /// and must have been authorised with the key the vote claims. That establishes the vote could
    /// only have come from a validator entitled to sign this docket. It does not re-verify the
    /// signature bytes, and <see cref="ProvenanceCheck.CheckedAgainst"/> says so rather than
    /// implying otherwise (FR-005).
    /// </para>
    /// <para>
    /// <b>No votes is <see cref="VerificationStatus.Unverified"/>, never
    /// <see cref="VerificationStatus.Verified"/>.</b> Single-validator deployments record no votes;
    /// there is no quorum evidence to check, and a green tick would report agreement that never
    /// happened (SC-005). It is not <see cref="VerificationStatus.Failed"/> either — that
    /// configuration is supported, not broken.
    /// </para>
    /// </remarks>
    private static ProvenanceCheck CheckSigners(DocketEvidence docket, RosterAsOf? rosterAsOf)
    {
        var against = rosterAsOf is null
            ? "the validator roster as it stood at this docket (could not be resolved)"
            : $"the validator roster as it stood at this docket — version {rosterAsOf.RosterVersion}, " +
              $"established by {rosterAsOf.ResolvedFrom}";

        if (docket.Votes.Count == 0)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Signers,
                Status = VerificationStatus.Unverified,
                Headline = "No quorum evidence was recorded for this docket",
                CheckedAgainst = against,
                Reason =
                    "this docket carries no consensus votes, so there is no quorum evidence to check. " +
                    "That is expected on a single-validator deployment, which seals dockets without " +
                    "recording votes",
            };
        }

        if (rosterAsOf is null || rosterAsOf.Entries.Count == 0)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Signers,
                Status = VerificationStatus.Unverified,
                Headline = "Validator set at this docket could not be established",
                CheckedAgainst = against,
                Detail = $"{docket.Votes.Count} vote(s) recorded",
                Reason =
                    "the validator roster version applying at this docket could not be resolved, so " +
                    "there is nothing to judge these signatures against",
            };
        }

        var unattributed = new List<string>();
        var unrecordedKeys = new List<string>();

        foreach (var vote in docket.Votes)
        {
            if (string.IsNullOrWhiteSpace(vote.PublicKey))
            {
                // Missing evidence, not contradicted evidence — the vote records no key to attribute.
                unrecordedKeys.Add(vote.ValidatorId);
                continue;
            }

            var entitled = rosterAsOf.Entries.Any(entry =>
                entry.HeldAuthority &&
                string.Equals(entry.ValidatorId, vote.ValidatorId, StringComparison.Ordinal) &&
                string.Equals(entry.PublicKey, vote.PublicKey, StringComparison.Ordinal));

            if (!entitled)
            {
                unattributed.Add(vote.ValidatorId);
            }
        }

        // A contradiction outranks an absence: if any vote demonstrably could not have been made by
        // an entitled validator, that is a failure regardless of how many others were merely unrecorded.
        if (unattributed.Count > 0)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Signers,
                Status = VerificationStatus.Failed,
                Headline = unattributed.Count == 1
                    ? "A signer did not hold authority at this docket"
                    : $"{unattributed.Count} signers did not hold authority at this docket",
                CheckedAgainst = against,
                Detail =
                    $"Not entitled to sign this docket: {string.Join(", ", unattributed)} · " +
                    $"roster version {rosterAsOf.RosterVersion} held: " +
                    $"{string.Join(", ", rosterAsOf.Entries.Where(e => e.HeldAuthority).Select(e => e.ValidatorId))}",
            };
        }

        if (unrecordedKeys.Count > 0)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Signers,
                Status = VerificationStatus.Unverified,
                Headline = "Some votes record no signing key",
                CheckedAgainst = against,
                Detail = $"No key recorded for: {string.Join(", ", unrecordedKeys)}",
                Reason =
                    "one or more recorded votes carry no signing key, so they cannot be attributed to " +
                    "a validator in the roster",
            };
        }

        return new ProvenanceCheck
        {
            Layer = ProvenanceLayer.Signers,
            Status = VerificationStatus.Verified,
            Headline = docket.Votes.Count == 1
                ? "The recorded signer held authority at this docket"
                : $"All {docket.Votes.Count} recorded signers held authority at this docket",
            CheckedAgainst = against,
            Detail =
                $"Signers: {string.Join(", ", docket.Votes.Select(v => v.ValidatorId))} · " +
                $"roster version {rosterAsOf.RosterVersion}",
        };
    }

    /// <summary>
    /// Was the proposing validator a member of the set applying when this docket sealed?
    /// </summary>
    /// <remarks>
    /// The same roster-as-of rule as <see cref="CheckSigners"/> (FR-010): a proposer removed later
    /// must still read as entitled for the dockets it proposed while it held authority. A docket
    /// sealed before Feature 187 made the proposer a first-class field records none, which is
    /// <see cref="VerificationStatus.Unverified"/> — blank and null alike, since the register stores
    /// the field as a non-nullable string defaulting to empty.
    /// </remarks>
    private static ProvenanceCheck CheckProposer(DocketEvidence docket, RosterAsOf? rosterAsOf)
    {
        var against = rosterAsOf is null
            ? "the validator roster as it stood at this docket (could not be resolved)"
            : $"the validator roster as it stood at this docket — version {rosterAsOf.RosterVersion}, " +
              $"established by {rosterAsOf.ResolvedFrom}";

        if (string.IsNullOrWhiteSpace(docket.ProposerValidatorId))
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Proposer,
                Status = VerificationStatus.Unverified,
                Headline = "No proposer was recorded for this docket",
                CheckedAgainst = against,
                Reason =
                    "this docket was sealed before the platform recorded which validator proposed it, " +
                    "so there is no proposer to check",
            };
        }

        if (rosterAsOf is null || rosterAsOf.Entries.Count == 0)
        {
            return new ProvenanceCheck
            {
                Layer = ProvenanceLayer.Proposer,
                Status = VerificationStatus.Unverified,
                Headline = "Validator set at this docket could not be established",
                CheckedAgainst = against,
                Detail = $"Proposer: {docket.ProposerValidatorId}",
                Reason =
                    "the validator roster version applying at this docket could not be resolved, so " +
                    "there is nothing to judge the proposer against",
            };
        }

        var entitled = rosterAsOf.Entries.Any(entry =>
            entry.HeldAuthority &&
            string.Equals(entry.ValidatorId, docket.ProposerValidatorId, StringComparison.Ordinal));

        return new ProvenanceCheck
        {
            Layer = ProvenanceLayer.Proposer,
            Status = entitled ? VerificationStatus.Verified : VerificationStatus.Failed,
            Headline = entitled
                ? "The proposer held authority at this docket"
                : "The proposer did NOT hold authority at this docket",
            CheckedAgainst = against,
            Detail =
                $"Proposer: {docket.ProposerValidatorId} · roster version {rosterAsOf.RosterVersion} held: " +
                $"{string.Join(", ", rosterAsOf.Entries.Where(e => e.HeldAuthority).Select(e => e.ValidatorId))}",
        };
    }
}
