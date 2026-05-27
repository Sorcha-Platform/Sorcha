// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Resolves the <c>formatMinimum</c> / <c>formatMaximum</c> constraints on a
/// <c>format: "date"</c> schema property into concrete <see cref="DateOnly"/>
/// bounds the UI can feed to the date-picker's <c>MinDate</c> / <c>MaxDate</c>.
/// Feature 107 Assured Identity v1 — DOB future-block (T013 / FR-001, FR-002).
/// </summary>
/// <remarks>
/// <para>
/// Accepts the Sorcha date-token vocabulary (<c>today</c>,
/// <c>today\u00B1N[D|M|Y]</c>) and ISO-8601 date literals. The parser is a
/// direct port of <c>Sorcha.Validator.Core.Tokens.SorchaDateTokenResolver</c>;
/// it lives in UI.Core rather than being pulled in by project reference
/// because UI.Core is Blazor WASM and avoiding the Validator.Core dependency
/// (Cryptography, TransactionHandler) keeps the client bundle small. If the
/// two implementations ever diverge, the server-side resolver remains
/// authoritative because the server is the source of truth for validation —
/// this client-side resolver drives a convenience-only UI hint.
/// </para>
/// </remarks>
public static class DateTimeFieldBoundResolver
{
    private static readonly Regex TokenPattern = new(
        @"^today(?:(?<sign>[+-])(?<n>\d{1,4})(?<unit>[DMY]))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads the named bound (<c>"formatMinimum"</c> or <c>"formatMaximum"</c>)
    /// from a property schema and resolves it against <paramref name="today"/>.
    /// Returns <see langword="false"/> and sets <paramref name="bound"/> to
    /// <see langword="null"/> when the bound is absent or unparseable.
    /// </summary>
    public static bool TryResolveBound(
        JsonElement propertySchema,
        string boundName,
        DateOnly today,
        out DateOnly? bound)
    {
        bound = null;

        if (propertySchema.ValueKind != JsonValueKind.Object)
            return false;

        // Contract: callers pass the literal strings "formatMinimum" /
        // "formatMaximum". Throw on typo so the bug surfaces in Release
        // builds (Blazor WASM ships Release; Debug.Assert is stripped) —
        // a silent no-op would un-bound the picker and let the citizen
        // pick a forbidden date with no client-side signal.
        if (boundName is not ("formatMinimum" or "formatMaximum"))
        {
            throw new ArgumentException(
                $"Unknown boundName '{boundName}' — expected 'formatMinimum' or 'formatMaximum'.",
                nameof(boundName));
        }

        if (!propertySchema.TryGetProperty(boundName, out var boundElement))
            return false;

        if (boundElement.ValueKind != JsonValueKind.String)
            return false;

        var raw = boundElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (TryResolveToken(raw, today, out var resolved))
        {
            bound = resolved;
            return true;
        }

        return false;
    }

    private static bool TryResolveToken(string token, DateOnly today, out DateOnly resolved)
    {
        resolved = default;

        // Fast path — ISO-8601 literal.
        if (DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var literal))
        {
            resolved = literal;
            return true;
        }

        var match = TokenPattern.Match(token);
        if (!match.Success) return false;

        if (!match.Groups["sign"].Success)
        {
            resolved = today;
            return true;
        }

        var sign = match.Groups["sign"].Value[0];
        var n = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value[0];
        var delta = sign == '+' ? n : -n;

        try
        {
            resolved = unit switch
            {
                'D' => today.AddDays(delta),
                'M' => today.AddMonths(delta),
                'Y' => today.AddYears(delta),
                _ => today
            };
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
