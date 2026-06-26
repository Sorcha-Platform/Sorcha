// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Models.Verification;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Supplies the catalogue of "what to verify" presets for the shared verify control
/// (Verify-unification PR B2). Decoupled from any hardcoded list so the question set is editable
/// via configuration (see <see cref="VerifierPresetsOptions"/>) without an application rewrite,
/// and so each host can supply its own catalogue.
/// </summary>
public interface IVerificationPresetCatalogue
{
    /// <summary>All available presets (configured set, or the builtin fallback when none configured).</summary>
    IReadOnlyList<VerificationPreset> GetAll();

    /// <summary>Looks up a preset by <see cref="VerificationPreset.Key"/>; null if not found.</summary>
    VerificationPreset? GetByKey(string? key);

    /// <summary>
    /// Builds an ad-hoc preset from a free-form custom request entered by the verifier. The
    /// known-claims set defaults to the union of required + optional claims.
    /// </summary>
    VerificationPreset BuildCustom(
        string purpose,
        string requiredVct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims);
}

/// <summary>
/// Configuration for the verification preset catalogue. Bind from the <c>"VerifierPresets"</c>
/// section; when empty, <see cref="DefaultPresetCatalogue"/> falls back to its builtin presets.
/// </summary>
public sealed class VerifierPresetsOptions
{
    /// <summary>The configuration section name to bind from.</summary>
    public const string SectionName = "VerifierPresets";

    /// <summary>The configured presets. Empty means "use the builtin fallback".</summary>
    public List<VerificationPresetConfig> Presets { get; set; } = new();
}

/// <summary>
/// Mutable, configuration-bindable shape of a <see cref="VerificationPreset"/> (records with
/// positional constructors do not bind cleanly from <c>IConfiguration</c>).
/// </summary>
public sealed class VerificationPresetConfig
{
    /// <summary>Stable identifier for the preset.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Human-facing label.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>Purpose string shown to the holder.</summary>
    public string Purpose { get; set; } = string.Empty;
    /// <summary>The required credential type (VCT).</summary>
    public string RequiredVct { get; set; } = string.Empty;
    /// <summary>Claims that must be disclosed.</summary>
    public List<string> RequiredClaims { get; set; } = new();
    /// <summary>Claims that are optional.</summary>
    public List<string> OptionalClaims { get; set; } = new();
    /// <summary>All claims the credential is known to carry.</summary>
    public List<string> KnownCredentialClaims { get; set; } = new();
}
