// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine;

/// <summary>
/// The ordered set of checks for a single subject — in Phase 1, one sealed docket.
/// </summary>
/// <remarks>
/// <para>
/// Order is presentation order, broadest first: Anchor, Chain, Seal, Signers, Proposer. It follows
/// <see cref="ProvenanceLayer"/>'s declaration order and is stable, so a reader (and a test) can
/// rely on position.
/// </para>
/// <para>
/// A trail is not itself a signed object and makes no overall claim. There is deliberately no
/// "overall pass" property: collapsing five differently-grounded results into one tick is how a
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> row gets read as
/// success. Consumers that need a summary must state which layers they folded in.
/// </para>
/// </remarks>
public sealed record ProvenanceTrail
{
    /// <summary>The register the subject docket belongs to.</summary>
    public required string RegisterId { get; init; }

    /// <summary>The docket this trail describes.</summary>
    public required ulong DocketNumber { get; init; }

    /// <summary>One check per <see cref="ProvenanceLayer"/>, in declaration order.</summary>
    public required IReadOnlyList<ProvenanceCheck> Checks { get; init; }
}
