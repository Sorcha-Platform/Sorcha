// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Evidence;

/// <summary>
/// The validator set as it stood <b>when a given docket was sealed</b> — not as it stands now.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the centre of gravity of the whole feature (plan D5, research R-005).</b>
/// FR-010 requires each signature to be checked against the membership that applied at that docket.
/// Judging history against the present set produces a <i>false failure</i>, which is strictly worse
/// than no check at all: it reports tampering where there is none, and it does so more often the
/// more the network grows — precisely when this feature matters most.
/// </para>
/// <para>
/// <b>The engine is never handed the current roster.</b> Not as a convention, but structurally:
/// there is no parameter it could arrive through. Resolving the applicable version is the service's
/// job (<c>RosterAsOfResolver</c>), and it is the only component permitted to see the full roster
/// history. Withholding the current roster removes the possibility of the naive implementation
/// rather than relying on the next author's discipline.
/// </para>
/// <para>
/// A null <c>RosterAsOf</c> means the applicable version could not be resolved — the Signers and
/// Proposer checks then report
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>, never
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Failed"/>.
/// </para>
/// </remarks>
public sealed record RosterAsOf
{
    /// <summary>The roster version that applied at the subject docket.</summary>
    public required int RosterVersion { get; init; }

    /// <summary>
    /// Validator membership as it stood then. Empty means the version resolved but recorded no
    /// members, which the consuming checks treat as unresolvable rather than as an empty set that
    /// nobody belongs to.
    /// </summary>
    public required IReadOnlyList<RosterEntryAsOf> Entries { get; init; }

    /// <summary>
    /// The control transaction that established this version, or a description of it. Feeds
    /// <see cref="ProvenanceCheck.CheckedAgainst"/>, so a reader can go and look at the record that
    /// set the membership rather than taking the result on trust.
    /// </summary>
    public required string ResolvedFrom { get; init; }
}
