// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.RegularExpressions;

namespace Sorcha.Blueprint.Models.Schemas;

/// <summary>
/// Resolves Sorcha date tokens used as cutoffs in <c>formatMinimum</c> /
/// <c>formatMaximum</c> constraints on <c>format: date</c> schema properties.
/// </summary>
/// <remarks>
/// <para>
/// JSON Schema 2020-12 defines <c>formatMinimum</c> and <c>formatMaximum</c> as
/// literal date strings. Sorcha extends the vocabulary with a tiny set of
/// relative tokens so that primitives like <c>DateOfBirth/v1</c> can express
/// "must be in the past" without hard-coding a calendar date that would be
/// wrong on day two.
/// </para>
/// <para>
/// Supported token grammar (all case-sensitive):
/// <list type="bullet">
///   <item><c>today</c> — the current date in the user's timezone.</item>
///   <item><c>today+{N}{D|M|Y}</c> — N days / months / years from today.</item>
///   <item><c>today-{N}{D|M|Y}</c> — N days / months / years before today.</item>
/// </list>
/// <c>now</c> and datetime-ranged tokens are intentionally reserved and NOT
/// implemented in this feature — they will be needed once <c>format: date-time</c>
/// primitives exist.
/// </para>
/// <para>
/// Any string that is a valid ISO-8601 date (<c>YYYY-MM-DD</c>) passes through
/// unchanged, so author-side literal cutoffs still work.
/// </para>
/// </remarks>
public static class SorchaDateTokenResolver
{
    // today | today[+-]N{D|M|Y} — N is bounded to 1-9999 so
    // DateOnly.Add{Years|Months|Days} can never overflow. Any realistic cutoff
    // (age gate, retention, validity window) fits inside 4 digits.
    private static readonly Regex TokenPattern = new(
        @"^today(?:(?<sign>[+-])(?<n>\d{1,4})(?<unit>[DMY]))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Resolves a token or literal date string against the supplied reference date.
    /// </summary>
    /// <param name="token">The token or ISO-8601 date literal.</param>
    /// <param name="today">The reference date to use for relative token resolution.</param>
    /// <returns>The resolved absolute date.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is null.</exception>
    /// <exception cref="FormatException">The input is neither a valid Sorcha token nor a valid ISO-8601 date.</exception>
    public static DateOnly Resolve(string token, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(token);

        // Fast path: literal ISO-8601 date.
        if (DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var literal))
        {
            return literal;
        }

        var match = TokenPattern.Match(token);
        if (!match.Success)
        {
            throw new FormatException(
                $"'{token}' is not a valid Sorcha date token or ISO-8601 date. " +
                "Expected 'today', 'today\u00B1N[D|M|Y]' (e.g. 'today-18Y', 'today+30D'), or 'YYYY-MM-DD'.");
        }

        // Bare 'today'
        if (!match.Groups["sign"].Success)
        {
            return today;
        }

        var sign = match.Groups["sign"].Value[0];
        var n = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value[0];

        var delta = sign == '+' ? n : -n;

        // Date arithmetic is capped by DateOnly's 0001-01-01..9999-12-31 range.
        // The regex allows up to 4-digit magnitudes which is enough for every
        // realistic cutoff (age gates, retention windows), but large relative
        // offsets can still push a near-boundary today past MinValue or
        // MaxValue. Convert any overflow to a FormatException so callers see
        // a single consistent failure mode for "invalid date token".
        try
        {
            return unit switch
            {
                'D' => today.AddDays(delta),
                'M' => today.AddMonths(delta),
                'Y' => today.AddYears(delta),
                _ => throw new FormatException($"Unsupported date token unit '{unit}'")
            };
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new FormatException(
                $"Date token '{token}' produces a date outside the supported range " +
                $"({DateOnly.MinValue:yyyy-MM-dd}..{DateOnly.MaxValue:yyyy-MM-dd}): {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Resolves a token against the current date in the supplied timezone (or
    /// UTC if none is supplied). Convenience helper for callers that don't have
    /// a reference date in hand.
    /// </summary>
    public static DateOnly Resolve(string token, TimeZoneInfo? timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var nowInTz = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        return Resolve(token, DateOnly.FromDateTime(nowInTz.Date));
    }
}
