// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Provenance;

/// <summary>
/// The outcome of one provenance check, as the client sees it.
/// </summary>
/// <remarks>
/// <b><see cref="Unverified"/> is not a failure and must never be rendered as one.</b> It means the
/// check could not run — a partial replica, a single-validator deployment, a docket predating the
/// evidence a check needs. Conflating it with <see cref="Failed"/> is the core misreading this
/// feature exists to prevent, so the two get visually distinct treatments (amber-neutral versus red)
/// rather than sharing a "not OK" style.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ProvenanceStatus>))]
public enum ProvenanceStatus
{
    /// <summary>The check could not run. Carries a reason. Never a veto.</summary>
    [JsonStringEnumMemberName("unverified")]
    Unverified,

    /// <summary>The check ran and passed.</summary>
    [JsonStringEnumMemberName("verified")]
    Verified,

    /// <summary>The check ran and did not pass.</summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}

/// <summary>One question asked of the evidence, and what came back.</summary>
/// <param name="Layer">Which question — anchor, chain, seal, signers, proposer.</param>
/// <param name="Status">Verified, failed, or unverified.</param>
/// <param name="Headline">One line the reader can act on.</param>
/// <param name="CheckedAgainst">What the comparison was made against. Always shown, never truncated away.</param>
/// <param name="Detail">The values behind the verdict, revealed on expand.</param>
/// <param name="Reason">Why the check could not run. Present when unverified.</param>
public sealed record ProvenanceCheckView(
    string Layer,
    ProvenanceStatus Status,
    string Headline,
    string CheckedAgainst,
    string? Detail,
    string? Reason);

/// <summary>A docket's ordered evidence trail.</summary>
/// <param name="RegisterId">The register.</param>
/// <param name="DocketNumber">The docket.</param>
/// <param name="Checks">Ordered broadest-first — anchor, chain, seal, signers, proposer.</param>
public sealed record ProvenanceTrailView(
    string RegisterId,
    ulong DocketNumber,
    IReadOnlyList<ProvenanceCheckView> Checks);

/// <summary>One docket on the spine.</summary>
/// <remarks>
/// Carries no status, deliberately — the spine runs no verification. See the server-side
/// <c>DocketSpineEntry</c> for why that is a design constraint rather than a gap.
/// </remarks>
/// <param name="DocketNumber">Docket height.</param>
/// <param name="SealedAt">When it was sealed.</param>
/// <param name="ProposerValidatorId">Who proposed it. Empty on dockets predating Feature 187.</param>
/// <param name="SignerCount">
/// Recorded consensus votes. Zero is valid and expected on single-validator deployments — it means
/// no quorum evidence was recorded, not that signatures are missing.
/// </param>
/// <param name="RosterChanged">The validator set changed here. This is network growth as history.</param>
public sealed record DocketSpineView(
    ulong DocketNumber,
    DateTime SealedAt,
    string ProposerValidatorId,
    int SignerCount,
    bool RosterChanged);

/// <summary>A page of a register's spine.</summary>
/// <param name="RegisterId">The register.</param>
/// <param name="Dockets">Docket summaries in order.</param>
/// <param name="HasMore">Whether further dockets follow.</param>
/// <param name="NextFromDocket">Where to resume, when there is more.</param>
public sealed record RegisterSpinePage(
    string RegisterId,
    IReadOnlyList<DocketSpineView> Dockets,
    bool HasMore,
    ulong? NextFromDocket);
