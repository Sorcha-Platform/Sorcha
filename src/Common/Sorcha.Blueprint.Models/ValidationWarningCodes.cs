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
/// Service-internal validator codes (e.g. <c>VAL_BP_010</c>,
/// <c>VAL_BP_CRED_*</c>) live alongside the rules that emit them and are
/// not in scope here.
/// </para>
/// </remarks>
public static class ValidationWarningCodes
{
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
}
