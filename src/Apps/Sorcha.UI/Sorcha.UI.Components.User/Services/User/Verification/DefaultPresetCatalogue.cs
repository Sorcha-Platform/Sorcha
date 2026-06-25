// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.UI.Components.User.Models.Verification;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Config-driven <see cref="IVerificationPresetCatalogue"/> (Verify-unification PR B2). Returns the
/// presets bound from the <c>"VerifierPresets"</c> configuration section; when that section is absent
/// or empty, falls back to the builtin presets. This is what lets the verification question set be
/// edited via configuration without an application change.
/// </summary>
public sealed class DefaultPresetCatalogue : IVerificationPresetCatalogue
{
    private const string AssuredIdentityVct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>
    /// Builtin presets, used when no presets are configured. Mirrors the desk verifier's original
    /// hardcoded set so behaviour is preserved until a deployment supplies its own catalogue.
    /// </summary>
    internal static readonly IReadOnlyList<VerificationPreset> Builtin = new[]
    {
        new VerificationPreset(
            Key: "age-over-18",
            Label: "Age over 18?",
            Purpose: "Confirm the holder is over 18",
            RequiredVct: AssuredIdentityVct,
            RequiredClaims: new[] { "age_over_18", "portrait" },
            OptionalClaims: Array.Empty<string>(),
            KnownCredentialClaims: new[] { "age_over_18", "portrait", "fullName", "dateOfBirth" }),
        new VerificationPreset(
            Key: "confirm-identity",
            Label: "Confirm identity",
            Purpose: "Confirm the holder's identity",
            RequiredVct: AssuredIdentityVct,
            RequiredClaims: new[] { "fullName", "portrait" },
            OptionalClaims: new[] { "dateOfBirth" },
            KnownCredentialClaims: new[] { "age_over_18", "portrait", "fullName", "dateOfBirth" }),
    };

    private readonly IReadOnlyList<VerificationPreset> _presets;

    /// <summary>Creates the catalogue from bound options, applying the builtin fallback when empty.</summary>
    public DefaultPresetCatalogue(IOptions<VerifierPresetsOptions> options)
    {
        var configured = options.Value.Presets;
        _presets = configured is { Count: > 0 }
            ? configured.Select(Map).ToArray()
            : Builtin;
    }

    /// <inheritdoc />
    public IReadOnlyList<VerificationPreset> GetAll() => _presets;

    /// <inheritdoc />
    public VerificationPreset? GetByKey(string? key)
        => string.IsNullOrEmpty(key) ? null : _presets.FirstOrDefault(p => p.Key == key);

    /// <inheritdoc />
    public VerificationPreset BuildCustom(
        string purpose,
        string requiredVct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims)
    {
        var known = requiredClaims.Concat(optionalClaims).Distinct().ToArray();
        return new VerificationPreset(
            Key: "custom",
            Label: "Custom request",
            Purpose: purpose,
            RequiredVct: requiredVct,
            RequiredClaims: requiredClaims,
            OptionalClaims: optionalClaims,
            KnownCredentialClaims: known);
    }

    private static VerificationPreset Map(VerificationPresetConfig c) => new(
        Key: c.Key,
        Label: c.Label,
        Purpose: c.Purpose,
        RequiredVct: c.RequiredVct,
        RequiredClaims: c.RequiredClaims,
        OptionalClaims: c.OptionalClaims,
        KnownCredentialClaims: c.KnownCredentialClaims);
}
