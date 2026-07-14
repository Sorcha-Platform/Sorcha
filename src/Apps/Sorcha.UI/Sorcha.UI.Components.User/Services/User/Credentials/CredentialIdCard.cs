// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq;
using System.Text;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Forms;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Adapts a held credential to the shared <see cref="IdCardLayoutConfig"/> so identity credentials
/// render as the same styled ID card the citizen saw at application review, rather than a raw claims
/// table. The core <see cref="BuildConfig(string?, string?, string?, IReadOnlyDictionary{string, string?})"/>
/// is model-agnostic so BOTH the web detail dialog and the wallet PWA detail page produce an identical
/// card. Non-identity credentials fall back to each host's tabular view.
/// </summary>
public static class CredentialIdCard
{
    // Claims shown on the card face, in order, when present. Everything else — granular name parts,
    // the portrait blob, DIDs, status — belongs in the expandable technical details, not the face.
    private static readonly string[] FaceClaims = ["fullName", "dateOfBirth", "address", "email", "assuranceLevel"];

    private const string PortraitClaim = "portrait";

    /// <summary>
    /// True when the credential type denotes an identity credential (renders as an ID card). Pragmatic
    /// v1 heuristic — the type/vct name contains "identity" (e.g. AssuredIdentityCredential). A richer
    /// credential-type registry can replace this later without changing callers.
    /// </summary>
    public static bool IsIdentityCredential(string? credentialType)
        => !string.IsNullOrWhiteSpace(credentialType)
           && credentialType.Contains("identity", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds an <see cref="IdCardLayoutConfig"/> from a web detail view model. This is the third
    /// (and worst) door onto the n1 raw-digest leak: it used to read the RAW <see cref="CredentialDetailViewModel.Claims"/>
    /// dictionary and call <c>kv.Value?.ToString()</c> — for a boxed <see cref="System.Text.Json.JsonElement"/>
    /// of kind Object that returns <c>GetRawText()</c>, i.e. the exact <c>{"_sd":[...]}</c> blob. The
    /// ID-card face is the surface the bug report actually hit, since it renders <c>address</c> — a
    /// <see cref="FaceClaims"/> entry — for exactly the credential type (<c>AssuredIdentityCredential</c>)
    /// that shipped the bug.
    /// <para>
    /// Formats <see cref="CredentialDetailViewModel.Claims"/> through <see cref="CredentialClaimDisplayFormatter"/>
    /// directly, rather than trusting the pre-populated <see cref="CredentialDetailViewModel.DisplayClaims"/>
    /// field — the card is safe by construction here regardless of whether a given construction site
    /// (production mapper, test fixture, or a future caller) remembered to populate <c>DisplayClaims</c>.
    /// </para>
    /// </summary>
    public static IdCardLayoutConfig BuildConfig(CredentialDetailViewModel credential)
    {
        System.ArgumentNullException.ThrowIfNull(credential);
        var claims = CredentialClaimDisplayFormatter.BuildDisplayClaims(credential.Claims)
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value, System.StringComparer.Ordinal);
        var issuer = string.IsNullOrWhiteSpace(credential.IssuerName) ? null : credential.IssuerName;
        return BuildConfig(credential.Type, PrettyCredentialName(credential.Type), issuer, claims);
    }

    /// <summary>
    /// Model-agnostic card builder — used by both the web dialog and the wallet PWA page so the card is
    /// identical in both. <paramref name="claims"/> is the flat claim map (claimName → value).
    /// </summary>
    public static IdCardLayoutConfig BuildConfig(
        string? credentialType,
        string? credentialName,
        string? issuerName,
        IReadOnlyDictionary<string, string?> claims)
    {
        System.ArgumentNullException.ThrowIfNull(claims);

        var fieldValues = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        var pointers = new List<string>();

        // Prefer a computed fullName; otherwise synthesise "Given Family" from the granular parts so
        // the card always shows a name line rather than three separate name fields.
        if (!HasText(claims, "fullName"))
        {
            var joined = string.Join(" ", new[] { Text(claims, "givenName"), Text(claims, "familyName") }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(joined))
            {
                fieldValues["/fullName"] = joined;
                pointers.Add("/fullName");
            }
        }

        foreach (var key in FaceClaims)
        {
            var pointer = "/" + key;
            if (fieldValues.ContainsKey(pointer)) continue; // already synthesised (fullName)
            var text = Text(claims, key);
            if (string.IsNullOrWhiteSpace(text)) continue;
            fieldValues[pointer] = text;
            pointers.Add(pointer);
        }

        // Portrait → the sibling pointer IdCardLayout scans for the card photo.
        var portrait = Text(claims, PortraitClaim);
        if (!string.IsNullOrWhiteSpace(portrait))
        {
            fieldValues["/portrait/tokenImageBase64"] = portrait;
        }

        var section = new IdCardSection(Title: "Identity", OriginatingPageIndex: 0, FieldPointers: pointers);

        var name = string.IsNullOrWhiteSpace(credentialName) ? PrettyCredentialName(credentialType) : credentialName;
        return new IdCardLayoutConfig
        {
            // The issuer org name where known; otherwise the credential name carries the card identity.
            IssuerName = string.IsNullOrWhiteSpace(issuerName) ? name : issuerName!,
            CredentialName = name,
            ColourTheme = XReviewColourTheme.IdentityNavy,
            Watermark = IdCardWatermark.Issued,
            FieldValues = fieldValues,
            Sections = [section],
            Editable = false,
        };
    }

    private static bool HasText(IReadOnlyDictionary<string, string?> claims, string key)
        => !string.IsNullOrWhiteSpace(Text(claims, key));

    private static string? Text(IReadOnlyDictionary<string, string?> claims, string key)
        => claims.TryGetValue(key, out var v) ? v : null;

    // "AssuredIdentityCredential" -> "Assured Identity". Strips a trailing "Credential", splits camelCase.
    private static string PrettyCredentialName(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Credential";
        var trimmed = type.EndsWith("Credential", System.StringComparison.OrdinalIgnoreCase) && type.Length > 10
            ? type[..^10]
            : type;
        var sb = new StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (i > 0 && char.IsUpper(trimmed[i]) && !char.IsUpper(trimmed[i - 1])) sb.Append(' ');
            sb.Append(trimmed[i]);
        }
        return sb.ToString().Trim();
    }
}
