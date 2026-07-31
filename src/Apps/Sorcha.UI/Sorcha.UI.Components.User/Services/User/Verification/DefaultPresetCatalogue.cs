// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.CitizenWallet.Abstractions.Constants;
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
    private const string AssuredIdentityVct = VctUris.AssuredIdentityV1;
    private const string CyberLevelVct = VctUris.CyberLevelV1;

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
        // AIAS M3. Without this the Cyber Level credential could be ISSUED but never REQUESTED —
        // this catalogue is what every verify surface drives, and it carried only the two Assured
        // Identity presets. Deliberately a builtin rather than per-deployment config: two of the
        // three surfaces (the web /app client and the wallet PWA) are Blazor WebAssembly, so their
        // configuration is a wwwroot/appsettings.json baked into the image — "just configure it"
        // is not a deployment knob there, and the kiosk sets no VerifierPresets either.
        //
        // level + portrait are BOTH required because the design's claim is a verdict showing
        // "photo + selective level disclosure"; a verdict missing either is not that claim. The
        // name claims stay optional so a holder can prove their level without identifying
        // themselves. Claim names track the aias-cyber-level template's `disclosable` set.
        new VerificationPreset(
            Key: "cyber-level",
            Label: "Cyber level",
            Purpose: "Confirm the holder's certified cyber level",
            RequiredVct: CyberLevelVct,
            RequiredClaims: new[] { "level", "portrait" },
            OptionalClaims: new[] { "givenName", "familyName" },
            KnownCredentialClaims: new[] { "level", "portrait", "givenName", "familyName" }),
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
