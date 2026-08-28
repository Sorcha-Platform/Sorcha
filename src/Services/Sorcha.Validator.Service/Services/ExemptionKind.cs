// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// The administrative transaction kinds that may be granted the six validation exemptions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Feature 196 / issue #1591.</b> An enumeration rather than a set of string comparisons, so a
/// new claimable value cannot be introduced without being classified. Every value MUST have an
/// authority rule in <see cref="ExemptionAuthorityResolver"/>; a value with no rule is exactly the
/// defect this feature removes, and <c>ExemptionKindCoverageTests</c> fails the build on one.
/// </para>
/// <para>
/// The six exemptions are: action-schema validation, blueprint conformance (including
/// <c>VAL_BP_002</c> sender authorisation), routing-decision attestation, crypto policy, sequence
/// replay, and — through the persisted transaction type — fork detection.
/// </para>
/// <para>
/// <b>What this enum is NOT.</b> It does not describe what a transaction *is*; the wire carries
/// free-form labels for that. It names the three exemptions the validator can grant, so that
/// granting one is a decision about entitlement rather than a string match.
/// </para>
/// </remarks>
public enum ExemptionKind
{
    /// <summary>
    /// The network's genesis transaction. Authority: the signing key's fingerprint matches the
    /// node's trusted genesis anchor.
    /// </summary>
    Genesis,

    /// <summary>
    /// A governance control transaction. Authority: the signer is on the register's governance
    /// roster — the check that already existed, now load-bearing rather than coincidental.
    /// </summary>
    Control,

    /// <summary>
    /// A blueprint publication. Authority: the signer is on the register's validator roster under
    /// the register-control derivation context.
    /// </summary>
    BlueprintPublish
}
