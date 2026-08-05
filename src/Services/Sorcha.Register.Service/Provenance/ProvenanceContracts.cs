// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// One docket on the register's spine — the list surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Carries no check result, deliberately, and must not gain one</b> (plan D6, research R-006).
/// Verification is O(n) hashing per docket; a spine that verified eagerly would be O(n·m) on a list
/// view and SC-007 requires 5,000 dockets to stay usable. A status field here would invite someone
/// to populate it, which is precisely the cost the two-endpoint split exists to avoid — so the
/// absence is enforced by a test, not just by intent.
/// </para>
/// </remarks>
/// <param name="DocketNumber">Docket height.</param>
/// <param name="SealedAt">When the docket was sealed (UTC).</param>
/// <param name="ProposerValidatorId">
/// The validator that proposed it. Empty on dockets sealed before Feature 187 recorded a proposer.
/// </param>
/// <param name="SignerCount">
/// How many consensus votes were recorded. <b>Zero is valid and expected</b> on single-validator
/// deployments — it means no quorum evidence was recorded, not that signatures are missing or
/// invalid.
/// </param>
/// <param name="RosterChanged">
/// The validator set changed at this docket. This is what makes network growth visible as history
/// rather than inferred from a config file (FR-007).
/// </param>
public sealed record DocketSpineEntry(
    ulong DocketNumber,
    DateTime SealedAt,
    string ProposerValidatorId,
    int SignerCount,
    bool RosterChanged);

/// <summary>A page of a register's docket spine.</summary>
/// <param name="RegisterId">The register.</param>
/// <param name="Dockets">Docket summaries in order from the requested start.</param>
/// <param name="HasMore">Whether further dockets follow this page.</param>
/// <param name="NextFromDocket">The <c>fromDocket</c> value for the next page, when there is one.</param>
public sealed record RegisterSpineResponse(
    string RegisterId,
    IReadOnlyList<DocketSpineEntry> Dockets,
    bool HasMore,
    ulong? NextFromDocket);

/// <summary>One check in a docket's provenance trail, as returned on the wire.</summary>
/// <param name="Layer">Which question this answers.</param>
/// <param name="Status">verified / failed / unverified.</param>
/// <param name="Headline">One line the reader can act on.</param>
/// <param name="CheckedAgainst">What the comparison was made against.</param>
/// <param name="Detail">The values behind the verdict.</param>
/// <param name="Reason">Why the check could not run. Present when status is unverified.</param>
public sealed record ProvenanceCheckResponse(
    string Layer,
    string Status,
    string Headline,
    string CheckedAgainst,
    string? Detail,
    string? Reason);

/// <summary>A docket's provenance trail.</summary>
/// <param name="RegisterId">The register.</param>
/// <param name="DocketNumber">The docket.</param>
/// <param name="Checks">Ordered broadest-first — anchor, chain, seal, signers, proposer.</param>
public sealed record ProvenanceTrailResponse(
    string RegisterId,
    ulong DocketNumber,
    IReadOnlyList<ProvenanceCheckResponse> Checks);

/// <summary>Maps engine results onto the wire shape in <c>contracts/provenance-api.yaml</c>.</summary>
/// <remarks>
/// The contract specifies lowercase enum values (<c>verified</c>, <c>anchor</c>). They are mapped
/// explicitly rather than relying on serializer configuration, so a change to the service's JSON
/// options cannot silently alter a published contract.
/// </remarks>
internal static class ProvenanceContractMapper
{
    internal static ProvenanceTrailResponse ToResponse(ProvenanceTrail trail) => new(
        trail.RegisterId,
        trail.DocketNumber,
        trail.Checks.Select(ToResponse).ToList());

    private static ProvenanceCheckResponse ToResponse(ProvenanceCheck check) => new(
        Layer: Wire(check.Layer),
        Status: Wire(check.Status),
        Headline: check.Headline,
        CheckedAgainst: check.CheckedAgainst,
        Detail: check.Detail,
        Reason: check.Reason);

    internal static string Wire(ProvenanceLayer layer) => layer switch
    {
        ProvenanceLayer.Anchor => "anchor",
        ProvenanceLayer.Chain => "chain",
        ProvenanceLayer.Seal => "seal",
        ProvenanceLayer.Signers => "signers",
        ProvenanceLayer.Proposer => "proposer",
        _ => layer.ToString().ToLowerInvariant(),
    };

    internal static string Wire(VerificationStatus status) => status switch
    {
        VerificationStatus.Verified => "verified",
        VerificationStatus.Failed => "failed",
        VerificationStatus.Unverified => "unverified",
        _ => status.ToString().ToLowerInvariant(),
    };
}
