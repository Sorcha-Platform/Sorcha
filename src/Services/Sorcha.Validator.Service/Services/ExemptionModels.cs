// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Which unsigned surface a transaction used to claim an exemption. Recorded so an attempted bypass
/// names its own route (FR-013) — the two genesis routes are independent and closing one alone
/// closes nothing.
/// </summary>
public enum ExemptionClaimRoute
{
    /// <summary>No claim was made.</summary>
    None,

    /// <summary>Claimed via the <c>Metadata["Type"]</c> label.</summary>
    TypeLabel,

    /// <summary>Claimed via <c>BlueprintId == "genesis"</c>, without touching metadata at all.</summary>
    BlueprintIdentifier
}

/// <summary>
/// Why an exemption was refused. "Not entitled" and "could not tell" call for different operator
/// responses, so they are never collapsed into one reason.
/// </summary>
public enum ExemptionRefusalReason
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>No exemption was claimed. Ordinary traffic; not a refusal worth alarming on.</summary>
    NoClaim,

    /// <summary>A claim was made and the signer is demonstrably not entitled to it.</summary>
    NotEntitled,

    /// <summary>
    /// A claim was made and the authority source could not be consulted. Fails closed (FR-007):
    /// the exemption is withheld, because a node that cannot check has not checked.
    /// </summary>
    AuthorityUnresolvable
}

/// <summary>
/// What a transaction <b>asserts</b>. Untrusted: every field here is submitter-settable without
/// invalidating a signature.
/// </summary>
/// <param name="Kind">The exemption claimed, or null when none was claimed.</param>
/// <param name="Route">Which unsigned surface carried the claim.</param>
/// <param name="RawLabel">The raw label as submitted, for diagnostics.</param>
public readonly record struct ExemptionClaim(
    ExemptionKind? Kind,
    ExemptionClaimRoute Route,
    string? RawLabel)
{
    /// <summary>No exemption was claimed.</summary>
    public static ExemptionClaim None => new(null, ExemptionClaimRoute.None, null);

    /// <summary>Whether any exemption was claimed.</summary>
    public bool IsClaimed => Kind.HasValue;
}

/// <summary>
/// The single computed outcome of the exemption decision. <b>One producer only</b>:
/// <see cref="ExemptionAuthorityResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one value (Feature 196).</b> Before this, the grant was computed independently in
/// <c>TransactionTypeClassifier</c> and the compensating roster check in
/// <c>RightsEnforcementService</c>, and the system was correct only because those two happened to
/// agree. A single value cannot half-apply. This mirrors the pattern the codebase already enforces
/// for derivation contexts, cross-boundary validation codes, service addresses and publication ids:
/// a value that must be consistent gets exactly one producer.
/// </para>
/// </remarks>
/// <param name="Granted">Whether the six waivers apply.</param>
/// <param name="Kind">The kind granted, when granted.</param>
/// <param name="Claim">What was claimed, whether or not it was granted.</param>
/// <param name="RefusalReason">Why it was refused, when it was.</param>
/// <param name="Detail">Human-readable detail for the log line. Never surfaced to the submitter.</param>
public readonly record struct ExemptionDecision(
    bool Granted,
    ExemptionKind? Kind,
    ExemptionClaim Claim,
    ExemptionRefusalReason RefusalReason,
    string? Detail)
{
    /// <summary>The transaction claimed nothing; ordinary validation applies.</summary>
    public static ExemptionDecision NoClaim(ExemptionClaim claim) =>
        new(false, null, claim, ExemptionRefusalReason.NoClaim, null);

    /// <summary>The claim was corroborated by authority.</summary>
    public static ExemptionDecision Grant(ExemptionKind kind, ExemptionClaim claim) =>
        new(true, kind, claim, ExemptionRefusalReason.None, null);

    /// <summary>The signer is not entitled to the exemption they claimed.</summary>
    public static ExemptionDecision NotEntitled(ExemptionClaim claim, string detail) =>
        new(false, null, claim, ExemptionRefusalReason.NotEntitled, detail);

    /// <summary>The authority could not be consulted. Withheld, never granted (FR-007).</summary>
    public static ExemptionDecision Unresolvable(ExemptionClaim claim, string detail) =>
        new(false, null, claim, ExemptionRefusalReason.AuthorityUnresolvable, detail);

    /// <summary>
    /// True when a claim was made and refused. This — not an ordinary validation failure — is what
    /// an attempted exemption bypass looks like on the wire (FR-013).
    /// </summary>
    public bool IsRefusedClaim =>
        Claim.IsClaimed && !Granted && RefusalReason != ExemptionRefusalReason.NoClaim;

    /// <summary>
    /// True when the granted exemption is a genesis one — either the network's system-register
    /// genesis or an ordinary register's.
    /// </summary>
    /// <remarks>
    /// <b>Both, deliberately.</b> The two consumers of this — the short <c>GenesisMaxAge</c>
    /// freshness window, and <c>RightsEnforcementService</c>'s "a register has no roster until its
    /// genesis creates one" allowance — apply to any register's genesis, not just the system
    /// register's. Restricting it to <see cref="ExemptionKind.Genesis"/> is what stopped every new
    /// register from sealing its roster.
    /// </remarks>
    public bool IsGenesis =>
        Granted && Kind is ExemptionKind.Genesis or ExemptionKind.RegisterGenesis;
}
