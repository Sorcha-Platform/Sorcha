// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Models.Verification;

/// <summary>
/// A preset "what to verify" question for the shared verify control (Verify-unification PR B2).
/// Describes the credential type and claims a verifier asks a holder to present. The catalogue of
/// presets is supplied by <see cref="Services.Verification.IVerificationPresetCatalogue"/> and is
/// editable via configuration without an application change.
/// </summary>
/// <param name="Key">Stable identifier for the preset (e.g. <c>age-over-18</c>).</param>
/// <param name="Label">Human-facing label shown in the question picker.</param>
/// <param name="Purpose">The purpose string presented to the holder (why the data is requested).</param>
/// <param name="RequiredVct">The credential type (VCT) the presentation must be.</param>
/// <param name="RequiredClaims">Claims that MUST be disclosed for the verification to pass.</param>
/// <param name="OptionalClaims">Claims the verifier would like but does not require.</param>
/// <param name="KnownCredentialClaims">
/// All claims the credential is known to carry — used to compute the "withheld" set in the verdict
/// (known minus disclosed). May be empty.
/// </param>
public sealed record VerificationPreset(
    string Key,
    string Label,
    string Purpose,
    string RequiredVct,
    IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string> OptionalClaims,
    IReadOnlyList<string> KnownCredentialClaims);
