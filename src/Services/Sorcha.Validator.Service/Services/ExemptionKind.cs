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
    /// An ORDINARY register's genesis — the transaction that creates a new register and establishes
    /// its first roster. Authority: the register has no roster yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Distinct from <see cref="Genesis"/>, and missing that distinction broke every register
    /// creation on the network.</b> The first implementation assumed genesis existed only on the
    /// system register, so it required the one constant genesis transaction id and the system
    /// register id. But <c>RegisterCreationOrchestrator</c> marks EVERY new register's first
    /// transaction <c>Type=Genesis</c> with <c>BlueprintId="genesis"</c>. Those were refused the
    /// exemption, never sealed, and left each new register with an empty governance roster — which
    /// surfaces far away as a 403 "you do not hold a publish-governance role" on the first blueprint
    /// publish, naming a cause that has nothing to do with the real one.
    /// </para>
    /// <para>
    /// <b>Why "no roster yet" is the right authority.</b> A register genesis is the transaction that
    /// CREATES the roster, so there is nothing to check it against — the same chicken-and-egg
    /// <c>RightsEnforcementService</c> already resolves this way (F189 R-002). It is nonetheless a
    /// real constraint rather than a free pass: it can be claimed at most once per register, and
    /// never on a register that has already sealed a roster — which is where the value would be to an
    /// attacker. The unconditional grant it replaces applied to every register, forever.
    /// </para>
    /// </remarks>
    RegisterGenesis,

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
