// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Forms;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Adapts a held credential to the shared <see cref="IdCardLayoutConfig"/> so identity credentials
/// render as the same styled ID card the citizen saw at application review, rather than a raw claims
/// table. Non-identity credentials fall back to the tabular detail view.
/// </summary>
public static class CredentialIdCard
{
    // Claims shown on the card face, in order, when present. Everything else — granular name parts,
    // the portrait blob, DIDs, status — belongs in the expandable technical details, not the face.
    private static readonly string[] FaceClaims = ["fullName", "dateOfBirth", "address", "email", "assuranceLevel"];

    private const string PortraitClaim = "portrait";

    /// <summary>
    /// True when the credential type denotes an identity credential (renders as an ID card). Pragmatic
    /// v1 heuristic — the type name contains "identity" (e.g. AssuredIdentityCredential). A richer
    /// credential-type registry can replace this later without changing callers.
    /// </summary>
    public static bool IsIdentityCredential(string? credentialType)
        => !string.IsNullOrWhiteSpace(credentialType)
           && credentialType.Contains("identity", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds an <see cref="IdCardLayoutConfig"/> for a credential's card face.</summary>
    public static IdCardLayoutConfig BuildConfig(CredentialDetailViewModel credential)
    {
        System.ArgumentNullException.ThrowIfNull(credential);

        var claims = credential.Claims;
        var fieldValues = new Dictionary<string, object?>(System.StringComparer.Ordinal);
        var pointers = new List<string>();

        // Prefer a computed fullName; otherwise synthesise "Given Family" from the granular parts so
        // the card always shows a name line rather than three separate name fields.
        if (!ClaimHasText(claims, "fullName"))
        {
            var joined = string.Join(" ", new[] { ClaimText(claims, "givenName"), ClaimText(claims, "familyName") }
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
            var text = ClaimText(claims, key);
            if (string.IsNullOrWhiteSpace(text)) continue;
            fieldValues[pointer] = text;
            pointers.Add(pointer);
        }

        // Portrait → the sibling pointer IdCardLayout scans for the card photo.
        var portrait = ClaimText(claims, PortraitClaim);
        if (!string.IsNullOrWhiteSpace(portrait))
        {
            fieldValues["/portrait/tokenImageBase64"] = portrait;
        }

        var section = new IdCardSection(Title: "Identity", OriginatingPageIndex: 0, FieldPointers: pointers);

        return new IdCardLayoutConfig
        {
            // No friendly issuer name is threaded through the credential model yet (only the DID);
            // the prettified credential type carries the card identity. Issuer-org-name display is a
            // follow-up (resolve the DID or carry it at issuance).
            IssuerName = PrettyCredentialName(credential.Type),
            CredentialName = PrettyCredentialName(credential.Type),
            ColourTheme = XReviewColourTheme.IdentityNavy,
            Watermark = IdCardWatermark.Issued,
            FieldValues = fieldValues,
            Sections = [section],
            Editable = false,
        };
    }

    private static bool ClaimHasText(IReadOnlyDictionary<string, object> claims, string key)
        => !string.IsNullOrWhiteSpace(ClaimText(claims, key));

    private static string? ClaimText(IReadOnlyDictionary<string, object> claims, string key)
        => claims.TryGetValue(key, out var v) ? v?.ToString() : null;

    // "AssuredIdentityCredential" -> "Assured Identity". Strips a trailing "Credential", splits camelCase.
    private static string PrettyCredentialName(string type)
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
