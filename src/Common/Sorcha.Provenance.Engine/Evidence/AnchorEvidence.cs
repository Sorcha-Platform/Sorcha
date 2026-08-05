// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Evidence;

/// <summary>
/// What this node can say about where a register came from, and what it trusts.
/// </summary>
/// <remarks>
/// A null <c>AnchorEvidence</c>, or one with <see cref="IsAnchorKnown"/> false, yields
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> — a node that does
/// not know what it trusts cannot report that something is trustworthy, and must not report that it
/// is untrustworthy either.
/// </remarks>
public sealed record AnchorEvidence
{
    /// <summary>
    /// Whether this node holds a trust anchor at all.
    /// </summary>
    /// <remarks>
    /// False ⇒ the Anchor check is
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a reason.
    /// This is the correct outcome — not a failure — for a node whose anchor does not match the
    /// network or was never configured; see issue #1374, which tracks making the anchor
    /// independently configurable.
    /// </remarks>
    public required bool IsAnchorKnown { get; init; }

    /// <summary>The trust anchor this node holds, as a fingerprint. Null when unknown.</summary>
    public string? AnchorFingerprint { get; init; }

    /// <summary>
    /// The fingerprint recorded for this register's origin, to be compared against
    /// <see cref="AnchorFingerprint"/>. Null when the origin record does not carry one, which is
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>, not a mismatch.
    /// </summary>
    public string? OriginFingerprint { get; init; }

    /// <summary>
    /// What the origin record is, in words — e.g. "the system register's pre-signed genesis
    /// transaction". Feeds <see cref="ProvenanceCheck.CheckedAgainst"/> and
    /// <see cref="ProvenanceCheck.Detail"/> so the reader knows which record was consulted.
    /// </summary>
    public string? OriginDescription { get; init; }

    /// <summary>
    /// Why the origin could not be traced to the anchor, when that is a property of the register
    /// rather than of the node. Set by the assembler; surfaced verbatim as the check's reason.
    /// </summary>
    public string? UntraceableReason { get; init; }
}
