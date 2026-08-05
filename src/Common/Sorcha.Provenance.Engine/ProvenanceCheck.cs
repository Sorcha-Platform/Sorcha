// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine;

/// <summary>
/// One question asked of the evidence, and what came back.
/// </summary>
/// <remarks>
/// Every check reports exactly one <see cref="VerificationStatus"/> (FR-001) and every check states
/// what it was compared against (FR-002). A check that could not run reports
/// <see cref="VerificationStatus.Unverified"/> with a <see cref="Reason"/> — never
/// <see cref="VerificationStatus.Failed"/>, and never <see cref="VerificationStatus.Verified"/>
/// (FR-003, FR-004).
/// </remarks>
public sealed record ProvenanceCheck
{
    /// <summary>Which question this check answers.</summary>
    public required ProvenanceLayer Layer { get; init; }

    /// <summary>Verified, Failed, or Unverified. Exactly one, always (FR-001).</summary>
    public required VerificationStatus Status { get; init; }

    /// <summary>
    /// One line a reader can act on, e.g. "Contents match the sealed commitment" or
    /// "No quorum evidence recorded".
    /// </summary>
    public required string Headline { get; init; }

    /// <summary>
    /// What the comparison was made against, in terms the reader can restate in their own words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required, and not decorative.</b> This field is how FR-002 and FR-005 are met: a result a
    /// reader cannot explain the basis of is not evidence, it is an assertion. SC-006 is measured
    /// directly against it.
    /// </para>
    /// <para>
    /// <b>FR-005 — do not overstate what a check proves.</b> Where a check confirms that stored
    /// evidence is internally consistent rather than independently correct, this string must say so.
    /// For <see cref="ProvenanceLayer.Seal"/> it must read as <i>"recomputed from the docket's stored
    /// transaction ids"</i> and must not imply independent validation: recomputing a Merkle root with
    /// the same algorithm that sealed it proves the stored transaction list is unchanged since
    /// sealing — it does not prove the algorithm is correct, nor that the transactions themselves are
    /// genuine. A feature that overstates this is worse than no feature, because it manufactures
    /// confidence an auditor will rely on.
    /// </para>
    /// <para>
    /// The same discipline applies to <see cref="ProvenanceLayer.Signers"/>, which attributes each
    /// recorded vote to the validator set as it stood; see
    /// <see cref="DocketProvenanceVerifier"/> for what that does and does not establish.
    /// </para>
    /// </remarks>
    public required string CheckedAgainst { get; init; }

    /// <summary>The values behind the verdict. Shown when the reader expands the row.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Why the check could not run. Present whenever <see cref="Status"/> is
    /// <see cref="VerificationStatus.Unverified"/>, absent otherwise — an auditor needs to know
    /// <i>which</i> link could not be established (FR-020, SC-009).
    /// </summary>
    public string? Reason { get; init; }
}
