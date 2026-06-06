// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Text;
using System.Text.RegularExpressions;
using Sorcha.UI.Core.Models.Presentation;

namespace Sorcha.UI.Core.Components.Wallet;

/// <summary>
/// Derives the human-readable text shown on a wallet credential card / peek
/// from a <see cref="CachedCredential"/>. Pure and static so it can be unit
/// tested without rendering the (hub-heavy) Home page. Never surfaces a raw
/// DID or VCT URI to a citizen — those belong, collapsed, on the detail page.
/// </summary>
public static class CredentialDisplay
{
    private const string DemoIssuerDid = "did:sorcha:org:demo";

    /// <summary>True when the credential was minted by the demo issuer.</summary>
    public static bool IsDemo(CachedCredential c) =>
        string.Equals(c.IssuerDid, DemoIssuerDid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Eyebrow issuer line: demo → "sorcha.dev"; otherwise a shortened
    /// thumbprint of the issuer DID, or "Sorcha" when no DID is present.
    /// Never the full DID.
    /// </summary>
    public static string Issuer(CachedCredential c) =>
        IsDemo(c) ? "sorcha.dev"
        : string.IsNullOrWhiteSpace(c.IssuerDid) ? "Sorcha"
        : ShortenDid(c.IssuerDid!);

    /// <summary>Card name: an explicit DisplayLabel, else a humanized VCT.</summary>
    public static string Name(CachedCredential c) =>
        !string.IsNullOrWhiteSpace(c.DisplayLabel) ? c.DisplayLabel! : Humanize(c.Vct);

    /// <summary>
    /// Short type chip, or <c>null</c> to hide it. A wrong-looking chip
    /// (e.g. "ASSURE" truncated from a VCT) is worse than none.
    /// </summary>
    public static string? TypeChip(CachedCredential c)
    {
        if (IsDemo(c)) return "EU-ID";
        var v = c.Vct ?? string.Empty;
        if (v.Contains("identity", StringComparison.OrdinalIgnoreCase)) return "ID";
        if (v.Contains("driving", StringComparison.OrdinalIgnoreCase)
            || v.Contains("licence", StringComparison.OrdinalIgnoreCase)
            || v.Contains("license", StringComparison.OrdinalIgnoreCase)) return "DL";
        return null;
    }

    /// <summary>
    /// Turn a VCT (PascalCase type name or URI whose tail is one) into readable
    /// words, dropping a trailing "Credential" (the chip already conveys that).
    /// e.g. "AssuredIdentityCredential" → "Assured Identity".
    /// </summary>
    public static string Humanize(string? vct)
    {
        if (string.IsNullOrWhiteSpace(vct)) return "Credential";

        // Read the last URI segment so a VCT URI reads as its type name.
        var tail = vct;
        var sep = tail.LastIndexOfAny(['/', ':', '#']);
        if (sep >= 0 && sep < tail.Length - 1) tail = tail[(sep + 1)..];

        var spaced = SplitPascalCase(tail);
        if (spaced.Equals("Credential", StringComparison.OrdinalIgnoreCase))
            return "Credential";
        if (spaced.EndsWith(" Credential", StringComparison.OrdinalIgnoreCase))
            spaced = spaced[..^" Credential".Length];

        return spaced.Length == 0 ? "Credential" : spaced;
    }

    /// <summary>
    /// Derive a card status pill from a credential's expiry. No expiry known →
    /// presumed Valid (a held credential works until a status check says
    /// otherwise — revocation is a separate, status-list concern). Within
    /// 14 days → ExpiringSoon; already past → Revoked/"Expired".
    /// </summary>
    public static (CredentialStatus Status, string? Text) ExpiryStatus(
        DateTimeOffset? expiry, DateTimeOffset nowUtc)
    {
        if (expiry is null) return (CredentialStatus.Valid, null);
        if (expiry.Value <= nowUtc) return (CredentialStatus.Revoked, "Expired");

        var days = (int)Math.Ceiling((expiry.Value - nowUtc).TotalDays);
        if (days <= 14)
            return (CredentialStatus.ExpiringSoon, days <= 1 ? "Expires today" : $"Expires in {days} days");

        return (CredentialStatus.Valid, null);
    }

    // Matches a PascalCase credential-type token (ending in "Credential"),
    // optionally preceded by the article "a"/"an" so we can correct it.
    private static readonly Regex CredentialMention =
        new(@"\b(?:(a|an)\s+)?([A-Z][A-Za-z0-9]+Credential)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Humanize any raw PascalCase credential-type tokens embedded in a free-text
    /// string (e.g. a server-composed inbox title), fixing the preceding article.
    /// "…issued you a AssuredIdentityCredential" → "…issued you an Assured Identity
    /// credential". A no-op when the text carries no such token, so a friendly
    /// title written by the issuer passes through unchanged.
    /// </summary>
    public static string HumanizeCredentialMentions(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        return CredentialMention.Replace(text, m =>
        {
            var humanized = Humanize(m.Groups[2].Value);
            var phrase = $"{humanized} credential";
            if (!m.Groups[1].Success) return phrase;

            var startsVowel = humanized.Length > 0 && "aeiouAEIOU".IndexOf(humanized[0]) >= 0;
            var article = startsVowel ? "an" : "a";
            return $"{article} {phrase}";
        });
    }

    /// <summary>
    /// Shorten an identifier to first-8…last-5 of its meaningful tail
    /// (the part after the last ':'). e.g.
    /// "did:sorcha:org:WS11QRNV…HGNRK" → "WS11QRNV…HGNRK".
    /// </summary>
    public static string ShortenDid(string did)
    {
        if (string.IsNullOrWhiteSpace(did)) return "Sorcha";

        var id = did;
        var colon = id.LastIndexOf(':');
        if (colon >= 0 && colon < id.Length - 1) id = id[(colon + 1)..];

        return id.Length <= 15 ? id : $"{id[..8]}…{id[^5..]}";
    }

    private static string SplitPascalCase(string s)
    {
        var cleaned = s.Replace('-', ' ').Replace('_', ' ');
        var sb = new StringBuilder(cleaned.Length + 8);
        for (var i = 0; i < cleaned.Length; i++)
        {
            var ch = cleaned[i];
            var boundary = i > 0 && char.IsUpper(ch) &&
                (char.IsLower(cleaned[i - 1]) ||
                 (char.IsUpper(cleaned[i - 1]) && i + 1 < cleaned.Length && char.IsLower(cleaned[i + 1])));
            if (boundary) sb.Append(' ');
            sb.Append(ch);
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
