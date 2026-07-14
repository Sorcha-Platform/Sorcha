// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// The single safe path from a raw claim value (a plain string, or a boxed <see cref="JsonElement"/>
/// deserialized from the wallet API's ClaimsJson) to citizen-facing display text. An object or array
/// NEVER renders as raw JSON — that is how an unresolved SD-JWT digest array
/// (<c>{"_sd": ["zSH_kfTeW2Mlc", ...]}</c>) shipped to a citizen's phone on n1, rendered on the
/// <c>address</c> claim of an <c>AssuredIdentityCredential</c>. Protocol keys (top-level and nested,
/// anything starting with <c>_</c> — e.g. <c>_sd</c>, <c>_sd_alg</c>) are dropped at every level; a
/// genuine nested object (e.g. a structured address) renders as "Name: value" pairs instead.
/// <para>
/// Both render surfaces that show a credential's claim payload to a citizen share this formatter —
/// <see cref="CredentialApiService"/>'s detail-dialog <c>DisplayClaims</c> and
/// <see cref="CredentialIdCard"/>'s ID-card face — so there is exactly one place that decides how a
/// claim value becomes displayable text, not three independent (and divergent) copies of the same
/// "don't leak raw JSON" logic.
/// </para>
/// </summary>
public static class CredentialClaimDisplayFormatter
{
    /// <summary>
    /// Formats every non-protocol top-level claim into safe display text. Top-level keys starting
    /// with <c>_</c> are protocol keys and are dropped entirely.
    /// </summary>
    public static Dictionary<string, string> BuildDisplayClaims(IReadOnlyDictionary<string, object> claims)
    {
        return claims
            .Where(kvp => !kvp.Key.StartsWith('_'))
            .ToDictionary(kvp => kvp.Key, kvp => FormatClaimForDetailDisplay(kvp.Value));
    }

    /// <summary>
    /// Formats a single claim value. Nested objects render as "Name: value" pairs, recursively,
    /// dropping <c>_</c>-prefixed protocol keys at every level — never raw JSON.
    /// </summary>
    public static string FormatClaimForDetailDisplay(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        JsonElement el => FormatJsonElementForDetailDisplay(el),
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>Recursive <see cref="JsonElement"/> formatter backing <see cref="FormatClaimForDetailDisplay"/>.</summary>
    public static string FormatJsonElementForDetailDisplay(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.ToString(),
        JsonValueKind.True or JsonValueKind.False => el.GetBoolean().ToString(),
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Object => string.Join(", ", el.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => $"{HumaniseClaimName(p.Name)}: {FormatJsonElementForDetailDisplay(p.Value)}")),
        JsonValueKind.Array => string.Join(", ", el.EnumerateArray()
            .Select(FormatJsonElementForDetailDisplay)
            .Where(s => s.Length > 0)),
        _ => string.Empty
    };

    /// <summary>"dateOfBirth" → "Date of birth". Sentence case, not Title Case.</summary>
    public static string HumaniseClaimName(string key)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(
            key, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
