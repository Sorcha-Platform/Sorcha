// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// Canonical string constants for validator error codes. New rules MUST add their
/// code here and reference the constant rather than embedding a literal, so code
/// review and cross-file grepping stay honest.
/// </summary>
/// <remarks>
/// Legacy codes VAL_BP_001..003 are currently emitted as inline string literals
/// in <c>ValidationEngine.cs</c>. Opportunistic migration to this constants class
/// is welcome but not in scope for Feature 103.
/// </remarks>
public static class ValidationErrorCodes
{
    /// <summary>
    /// Blueprint publish rejected: a participant referenced as the sender of an
    /// <c>isStartingAction: true</c> action has a non-null <c>walletAddress</c>
    /// in the submitted blueprint. Starting-action participants MUST be left
    /// unbound so the runtime can late-bind the first qualifying submitter.
    /// </summary>
    /// <remarks>
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
    /// Blueprint publish <b>warning</b> (non-blocking): an action with
    /// <c>targetAudience == SorchaLocalWallet</c> does not declare an explicit
    /// recipient disclosure group for the credential payload, so the engine will
    /// synthesise a default one at mint time. Authors may ignore this warning or
    /// add an explicit disclosure to silence it.
    /// </summary>
    public const string SorchaLocalWalletImplicitDisclosure = "WARN_BP_CRED_002";

    /// <summary>
    /// Blueprint publish rejected: an action routes from a
    /// <c>SorchaLocalWallet</c> credential issuance into a next action whose
    /// <c>RejectionConfig.IsTerminal</c> is not set to <c>true</c>. Accept/decline
    /// flows require a clean terminal rejection path; non-terminal rejection on
    /// the accept action produces an ambiguous instance state.
    /// </summary>
    public const string SorchaLocalWalletRejectNotTerminal = "VAL_BP_CRED_003";

    // ---- Feature 107: Assured Identity v1 ----
    // Cross-service warning codes (ReviewLayoutUnknown, CredentialPortraitOversize)
    // live in Sorcha.Blueprint.Models.ValidationWarningCodes so Blueprint.Service
    // and Validator.Service can both reference them by symbol.
}
