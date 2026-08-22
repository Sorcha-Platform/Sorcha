// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Canonical string constants for validation warning codes shared between
/// services that publish or consume blueprint extensions. New codes that
/// cross service boundaries belong here so consumers can reference them by
/// symbol instead of duplicating string literals.
/// </summary>
/// <remarks>
/// <para>
/// Cross-boundary <b>error</b> codes have a sibling home in
/// <see cref="ValidationErrorCodes"/>. Genuinely service-internal codes
/// (the Validator's <c>VAL_SCHEMA_*</c>, <c>VAL_STRUCT_*</c>, <c>VAL_PERM_*</c>
/// families) still live alongside the rules that emit them and are not in
/// scope for either class.
/// </para>
/// </remarks>
public static class ValidationWarningCodes
{
    /// <summary>
    /// Blueprint publish <b>warning</b> (non-blocking): an action with
    /// <c>targetAudience == SorchaLocalWallet</c> does not declare an explicit
    /// recipient disclosure group for the credential payload, so the engine will
    /// synthesise a default one at mint time. Authors may ignore this warning or
    /// add an explicit disclosure to silence it.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/106-register-native-credentials/contracts/credential-issuance-config.md</c>.
    /// </remarks>
    public const string SorchaLocalWalletImplicitDisclosure = "WARN_BP_CRED_002";

    /// <summary>
    /// Blueprint publish <b>warning</b> (non-blocking): an <c>x-review</c> page
    /// declares a <c>layout</c> value the renderer does not recognise. The blueprint
    /// still publishes and the UI falls back to a tabular minimal review rather than
    /// the requested card variant.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/107-assured-identity-v1/contracts/x-review-extension.md</c>.
    /// </remarks>
    public const string ReviewLayoutUnknown = "WARN_BP_REVIEW_001";

    /// <summary>
    /// Credential issuance <b>warning</b> (non-blocking): the portrait
    /// <c>tokenImageBase64</c> source field exceeded the ~27KB base64 size bound.
    /// The credential is still issued but the <c>portrait</c> claim is omitted;
    /// the citizen is surfaced a warning so they can re-capture if desired.
    /// </summary>
    /// <remarks>
    /// Contract: <c>specs/107-assured-identity-v1/contracts/portrait-claim-format.md</c>.
    /// </remarks>
    public const string CredentialPortraitOversize = "WARN_CRED_PORTRAIT_OVERSIZE_001";

    /// <summary>
    /// Blueprint publish <b>warning</b> (non-blocking): an action declares a
    /// <c>credentialIssuanceConfig</c> with no <c>issuanceCondition</c>, yet routes on a
    /// decision (a conditional route, or more than one route). Minting runs <b>before</b>
    /// routing, so the credential is minted <i>and delivered</i> on every path the action
    /// can take - including a terminal reject route. A reject route stops the credential
    /// being handed over; it does not stop it being issued.
    /// </summary>
    /// <remarks>
    /// Issue #1551. Three shipped blueprints had this shape, so a declined applicant was
    /// issued a credential. Silence it by adding an <c>issuanceCondition</c> (JSON Logic
    /// over the submitted action data), or by removing the decision routing.
    /// </remarks>
    public const string UnconditionalIssuanceOnDecision = "WARN_BP_CRED_005";
}
