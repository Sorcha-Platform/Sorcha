// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.UI.Components.User.Models.Verification;

/// <summary>
/// View model for the verdict screen (Feature 163). Maps a <see cref="VerificationOutcome"/> + the
/// originating preset question into the headline verdict, the issuer identity, the disclosed/withheld
/// split, and the validation-trail rows. The RegisterAnchor layer is appended on demand by the
/// <c>VerdictTrailPanel</c> component after the operator triggers the layer-4 check.
/// </summary>
public sealed class VerdictViewModel
{
    /// <summary>Overall pass — accepted AND no layer actively failed. An Unverified layer never vetoes.</summary>
    public bool OverallPass { get; private init; }

    /// <summary>Headline shown on the card, e.g. "Over 18" / "Not over 18" / "Verified".</summary>
    public string Headline { get; private init; } = "";

    /// <summary>Issuer DID (the credential's <c>iss</c>), surfaced for the operator's trust judgement.</summary>
    public string? IssuerDid { get; private init; }

    /// <summary>Best-effort issuer display name (falls back to the DID).</summary>
    public string? IssuerDisplayName { get; private init; }

    /// <summary>Base64 portrait token, when disclosed.</summary>
    public string? PortraitBase64 { get; private init; }

    /// <summary>The age-over-18 chip value, when the claim was disclosed.</summary>
    public bool? AgeOver18 { get; private init; }

    /// <summary>Disclosed claim name → stringified value (portrait elided to a marker).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Disclosed { get; private init; } = [];

    /// <summary>Known credential claims that were NOT disclosed — the visible proof of minimal disclosure.</summary>
    public IReadOnlyList<string> Withheld { get; private init; } = [];

    /// <summary>The validation-trail rows (mutable so the component can append/replace the RegisterAnchor layer).</summary>
    public List<ValidationLayerResult> Layers { get; } = [];

    /// <summary>Rejection reasons (shown on a fail verdict).</summary>
    public IReadOnlyList<string> Errors { get; private init; } = [];

    /// <summary>The register anchor reference (registerId) read from the credential, for the layer-4 check.</summary>
    public string? RegisterAnchorId { get; private init; }

    /// <summary>The credential id (jti) read from the disclosed claims / outcome, for the layer-4 check.</summary>
    public string? CredentialId { get; private init; }

    /// <summary>True when the asked preset is an age-threshold check — drives the age hero treatment.</summary>
    public bool IsAgeTreatment { get; private init; }

    /// <summary>The minimal-disclosure statement shown on the age treatment; null for the identity treatment.</summary>
    public string? MinimalDisclosureNote { get; private init; }

    /// <summary>Whether the credential's issuer signature was cryptographically verified (drives warn vs pass).</summary>
    public bool IssuerSignatureVerified { get; private init; }

    private const string PortraitClaim = "portrait";
    private const string AgeClaim = "age_over_18";
    private const string AnchorClaim = "registerAnchor";

    /// <summary>
    /// SD-JWT / VC machine fields that must never appear in the operator-facing "Shared with you"
    /// list — they are credential plumbing (type, status, key binding, timestamps, anchor key), not
    /// disclosed identity attributes. Filtered from the display pairs; the raw disclosed set is still
    /// used for the portrait / age / anchor lookups.
    /// </summary>
    private static readonly HashSet<string> NonDisplayClaims = new(StringComparer.Ordinal)
    {
        "vct", "credentialStatus", "status", "cnf", "iss", "iat", "exp", "nbf",
        "sub", "jti", "id", "_sd", "_sd_alg", AnchorClaim,
    };

    /// <summary>
    /// Build the view model from a completed <paramref name="outcome"/> and the chosen
    /// <paramref name="question"/> preset. Known credential claims and required VCT come from the
    /// preset so the verdict can be computed client-side without a server session store (R-001).
    /// </summary>
    public static VerdictViewModel From(VerificationPreset question, VerificationOutcome outcome)
    {
        var disclosed = outcome.DisclosedClaims;
        var issuerDid = TryGetIssuerDid(outcome);

        bool? ageOver18 = disclosed.TryGetValue(AgeClaim, out var a) ? ToBool(a) : null;
        var portrait = disclosed.TryGetValue(PortraitClaim, out var p) ? p?.ToString() : null;

        var known = question.KnownCredentialClaims.Count > 0
            ? question.KnownCredentialClaims
            : (IReadOnlyList<string>)question.RequiredClaims;
        var withheld = known.Where(c => !disclosed.ContainsKey(c)).ToList();

        var disclosedPairs = disclosed
            .Where(kvp => !NonDisplayClaims.Contains(kvp.Key))
            // Skip structured (object/array) values — e.g. an undisclosed `address` surfaced as its
            // raw {"_sd":[…]} digest — which would dump machine JSON into the operator's view. The
            // portrait is exempt (it renders separately as a marker row).
            .Where(kvp => kvp.Key == PortraitClaim || !LooksLikeStructuredJson(kvp.Value))
            .Select(kvp => new KeyValuePair<string, string>(
                kvp.Key,
                kvp.Key == PortraitClaim ? "<portrait>" : kvp.Value?.ToString() ?? ""))
            .ToList();

        var headline = ageOver18 switch
        {
            true => "Over 18",
            false => "Not over 18",
            null => outcome.Accepted ? "Verified" : "Not verified",
        };

        var overallPass = outcome.Accepted && outcome.Layers.All(l => l.Status != LayerStatus.Fail);

        // The treatment follows the ASKED question, not what happened to be disclosed: an age-threshold
        // preset requires age_over_18 and does NOT ask for the name.
        var isAge = question.RequiredClaims.Contains(AgeClaim)
                    && !question.RequiredClaims.Contains("fullName");

        var vm = new VerdictViewModel
        {
            OverallPass = overallPass,
            Headline = headline,
            IssuerDid = issuerDid,
            IssuerDisplayName = issuerDid,
            PortraitBase64 = portrait,
            AgeOver18 = ageOver18,
            Disclosed = disclosedPairs,
            Withheld = withheld,
            Errors = outcome.Errors,
            RegisterAnchorId = disclosed.TryGetValue(AnchorClaim, out var r) ? r?.ToString() : null,
            CredentialId = TryGetCredentialId(outcome),
            IsAgeTreatment = isAge,
            MinimalDisclosureNote = isAge
                ? "You learned only that they're over 18 — and saw their photo to match the face. "
                  + "You did not learn their name, birth date, or exact age."
                : null,
            IssuerSignatureVerified = outcome.IssuerSignature == IssuerSignatureStatus.Verified,
        };
        vm.Layers.AddRange(outcome.Layers);
        return vm;
    }

    private static string? TryGetIssuerDid(VerificationOutcome outcome)
    {
        var issuerLayer = outcome.Layers.FirstOrDefault(l => l.Layer == ValidationLayer.IssuerSignature);
        return issuerLayer is not null && issuerLayer.Detail.TryGetValue("iss", out var iss) ? iss : null;
    }

    private static string? TryGetCredentialId(VerificationOutcome outcome)
    {
        var issuerLayer = outcome.Layers.FirstOrDefault(l => l.Layer == ValidationLayer.IssuerSignature);
        if (issuerLayer is not null && issuerLayer.Detail.TryGetValue("jti", out var jti) && !string.IsNullOrWhiteSpace(jti))
        {
            return jti;
        }
        if (outcome.DisclosedClaims.TryGetValue("jti", out var dj)) return dj?.ToString();
        if (outcome.DisclosedClaims.TryGetValue("id", out var di)) return di?.ToString();
        return null;
    }

    private static bool? ToBool(object? value) => value switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var b) => b,
        _ => null,
    };

    /// <summary>
    /// True when the value stringifies to a JSON object/array (starts with <c>{</c> or <c>[</c>) —
    /// the shape an undisclosed nested claim (e.g. a partially-disclosed address carrying its raw
    /// <c>_sd</c> digests) takes. Such values are machine plumbing, not a human-readable disclosure.
    /// </summary>
    private static bool LooksLikeStructuredJson(object? value)
    {
        var s = value?.ToString()?.TrimStart();
        return !string.IsNullOrEmpty(s) && (s[0] == '{' || s[0] == '[');
    }
}
