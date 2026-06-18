// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verifier.Services;

/// <summary>
/// A preset verification question the operator can ask (Feature 155). Each preset maps a friendly
/// label to the credential type (<see cref="RequiredVct"/>) and the minimal set of claims requested,
/// so an over-18 check discloses only <c>age_over_18</c> + <c>portrait</c> and nothing else.
/// </summary>
/// <param name="Key">Stable key, e.g. <c>age-over-18</c>.</param>
/// <param name="Label">Operator-facing label, e.g. "Age over 18?".</param>
/// <param name="Purpose">Purpose shown to the citizen on the consent sheet.</param>
/// <param name="RequiredVct">Credential type the citizen must present.</param>
/// <param name="RequiredClaims">Claims the citizen must disclose.</param>
/// <param name="OptionalClaims">Claims the citizen may disclose.</param>
/// <param name="KnownCredentialClaims">
/// The full set of claims this credential type is known to carry — used purely to compute the
/// "withheld" list on the verdict (issued-but-not-requested), making minimal disclosure visible.
/// </param>
public sealed record QuestionPreset(
    string Key,
    string Label,
    string Purpose,
    string RequiredVct,
    IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string> OptionalClaims,
    IReadOnlyList<string> KnownCredentialClaims);

/// <summary>Built-in question presets for the Ask screen.</summary>
public static class QuestionPresets
{
    /// <summary>The AssuredIdentity credential type used by the demo presets.</summary>
    public const string AssuredIdentityVct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>The full AssuredIdentity attribute set, used to compute withheld claims.</summary>
    public static readonly IReadOnlyList<string> AssuredIdentityClaims =
    [
        "age_over_18", "portrait", "givenName", "familyName", "fullName",
        "dateOfBirth", "email", "address",
    ];

    /// <summary>The <c>age-over-18</c> preset — discloses only the boolean answer + portrait.</summary>
    public static readonly QuestionPreset AgeOver18 = new(
        Key: "age-over-18",
        Label: "Age over 18?",
        Purpose: "Confirm the holder is over 18",
        RequiredVct: AssuredIdentityVct,
        RequiredClaims: ["age_over_18", "portrait"],
        OptionalClaims: [],
        KnownCredentialClaims: AssuredIdentityClaims);

    /// <summary>The <c>confirm-identity</c> preset — name + portrait for a staffed identity check.</summary>
    public static readonly QuestionPreset ConfirmIdentity = new(
        Key: "confirm-identity",
        Label: "Confirm identity",
        Purpose: "Confirm the holder's identity",
        RequiredVct: AssuredIdentityVct,
        RequiredClaims: ["fullName", "portrait"],
        OptionalClaims: ["dateOfBirth"],
        KnownCredentialClaims: AssuredIdentityClaims);

    /// <summary>All built-in presets (excluding the free-form custom option, which the UI adds).</summary>
    public static readonly IReadOnlyList<QuestionPreset> All = [AgeOver18, ConfirmIdentity];

    /// <summary>Look up a preset by key, or null for unknown / custom.</summary>
    public static QuestionPreset? ByKey(string? key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
