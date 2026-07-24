// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Canonical string constants for validation <b>error</b> codes that cross a service boundary —
/// the sibling of <see cref="ValidationWarningCodes"/>, and governed by the same rule: a code
/// emitted by one project and named by another belongs here, so both sides reference one symbol
/// instead of typing the same string twice.
/// </summary>
/// <remarks>
/// <para>
/// This lives in <c>Sorcha.Blueprint.Models</c> — a zero-dependency leaf already referenced by
/// Blueprint.Service, Validator.Service and Validator.Core — because those are exactly the
/// projects that produce and consume these codes.
/// </para>
/// <para>
/// <b>Why a literal here is worse than it looks.</b> Some of these codes are not merely logged;
/// they are <i>matched on</i>. <see cref="ChainFork"/> is compared by string in Blueprint
/// Service's presentation seal coordinator to recognise "already sealed via another path" and
/// dedupe silently. Rename the producer's literal and that comparison simply stops matching: no
/// compile error, no exception, no log — just a duplicate-submission path that quietly stops
/// being deduped. The compiler cannot help across two independently-typed string literals; a
/// shared constant is what makes it help.
/// </para>
/// <para>
/// <b>Scope.</b> Service-internal codes stay with the rule that emits them, per the convention
/// already recorded on <see cref="ValidationWarningCodes"/>. The Validator's ~70 internal codes
/// (<c>VAL_SCHEMA_*</c>, <c>VAL_STRUCT_*</c>, <c>VAL_PERM_*</c>, …) are declared and consumed in
/// the same file and carry no cross-boundary drift risk, so they are deliberately not hoisted
/// here. Only promote a code when a second project needs to name it.
/// </para>
/// <para>
/// Enforced by <c>scripts/check-error-code-contract.ps1</c> (CI: <c>error-code-contract-gate</c>),
/// which fails on a raw literal of any code declared here.
/// </para>
/// </remarks>
public static class ValidationErrorCodes
{
    // ---- Feature 103: Open participants & late binding ----

    /// <summary>
    /// Blueprint publish rejected: a participant referenced as the sender of an
    /// <c>isStartingAction: true</c> action has a non-null <c>walletAddress</c>
    /// in the submitted blueprint. Starting-action participants MUST be left
    /// unbound so the runtime can late-bind the first qualifying submitter.
    /// </summary>
    /// <remarks>
    /// Emitted by Blueprint Service's publish-time guardrail.
    /// Contract: <c>specs/103-verified-citizen-v2/contracts/validator-publish-errors.md</c>.
    /// See also <c>.claude/skills/blueprint-builder/SKILL.md</c> — "Open Participants &amp; Late Binding".
    /// </remarks>
    public const string OpenParticipantPrebound = "VAL_BP_010";

    // ---- Feature 106: Register-native credential delivery ----

    /// <summary>
    /// Blueprint publish rejected: an action declares
    /// <c>credentialIssuanceConfig.targetAudience == SorchaLocalWallet</c> but its
    /// <c>recipientParticipantId</c> does not resolve to a participant declared on
    /// the blueprint. The publish-time check runs before the blueprint is sealed so
    /// authors get fast feedback rather than a runtime failure at issuance.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/106-register-native-credentials/contracts/credential-issuance-config.md</c>.
    /// </remarks>
    public const string SorchaLocalWalletRecipientUnknown = "VAL_BP_CRED_001";

    /// <summary>
    /// Blueprint publish rejected: an action routes from a
    /// <c>SorchaLocalWallet</c> credential issuance into a next action whose
    /// <c>RejectionConfig.IsTerminal</c> is not set to <c>true</c>. Accept/decline
    /// flows require a clean terminal rejection path; non-terminal rejection on
    /// the accept action produces an ambiguous instance state.
    /// </summary>
    public const string SorchaLocalWalletRejectNotTerminal = "VAL_BP_CRED_003";

    // ---- Chain integrity (Validator Service) ----

    /// <summary>
    /// Transaction rejected: its <c>previousTransactionId</c> points at a predecessor that has
    /// already been extended by a different transaction — i.e. submitting it would fork the chain.
    /// </summary>
    /// <remarks>
    /// <b>Load-bearing across services.</b> Blueprint Service's
    /// <c>RedisPresentationSealCoordinator</c> matches this code when draining a queued
    /// presentation submission (Feature 119) and treats it as "already sealed via another path",
    /// deduping silently rather than failing the presentation. That behaviour depends on the exact
    /// string, so both the Validator (producer) and the seal coordinator (consumer) must name it
    /// through this constant.
    /// </remarks>
    public const string ChainFork = "VAL_CHAIN_FORK";

    // ---- Revocation (Feature 079) ----

    /// <summary>
    /// Revocation transaction rejected: the revocation payload is malformed, targets an unknown or
    /// already-revoked transaction, or is not signed by a party entitled to revoke it.
    /// </summary>
    /// <remarks>
    /// Emitted from both <c>Sorcha.Validator.Core.RevocationValidator</c> (payload-level checks)
    /// and the Validator Service's <c>ValidationEngine</c> (transaction-level checks), which is why
    /// it is shared rather than local to either.
    /// </remarks>
    public const string RevocationInvalid = "VAL_REV_001";
}
