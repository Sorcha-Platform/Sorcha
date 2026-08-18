// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.Json;

using Json.Schema;

namespace Sorcha.Blueprint.Models.Schemas;

/// <summary>
/// Evaluates Sorcha's <c>formatMaximum</c> / <c>formatMinimum</c> bounds on
/// <c>format: date</c> values, so a declared date range is enforced by the validator rather
/// than merely bounding a date picker.
/// </summary>
/// <remarks>
/// <para>
/// The schema and the data are the truth; display is for humans. A bound that only the renderer
/// honours is not a constraint — anything posting straight to the API bypasses it, and worse, the
/// schema *looks* like it constrains the value. That is strictly more dangerous than declaring
/// nothing, because a blueprint author reads the schema and reasonably skips their own check.
/// </para>
/// <para>
/// Measured on n1 2026-08-17: the validator enforced every standard keyword these primitives use
/// (<c>format: date</c> including calendar validity, <c>format: email</c>, <c>minLength</c>,
/// <c>maxLength</c>) and silently accepted <c>dateOfBirth = 2035-06-15</c>, sealing it into a
/// docket — because <c>formatMaximum</c> is not a JSON Schema keyword and unknown keywords are
/// annotations.
/// </para>
/// <para>
/// Scope is deliberately narrow. This handler reports a violation ONLY when the instance is a
/// string that parses as an ISO-8601 date and falls outside the bound:
/// <list type="bullet">
///   <item>a non-string instance is left to <c>type</c>;</item>
///   <item>a string that is not a valid date is left to <c>format</c> — reporting it here too
///         would produce two errors for one defect;</item>
///   <item>an unparseable bound is reported against the SCHEMA, since a cutoff nobody can resolve
///         must not silently pass everything.</item>
/// </list>
/// Bounds are inclusive: <c>formatMaximum: "today"</c> accepts today.
/// </para>
/// </remarks>
public sealed class FormatBoundKeywordHandler : IKeywordHandler
{
    /// <summary>The <c>formatMaximum</c> keyword name.</summary>
    public const string MaximumKeyword = "formatMaximum";

    /// <summary>The <c>formatMinimum</c> keyword name.</summary>
    public const string MinimumKeyword = "formatMinimum";

    private readonly bool _isMaximum;

    /// <summary>Handler for <c>formatMaximum</c>.</summary>
    public static FormatBoundKeywordHandler Maximum { get; } = new(isMaximum: true);

    /// <summary>Handler for <c>formatMinimum</c>.</summary>
    public static FormatBoundKeywordHandler Minimum { get; } = new(isMaximum: false);

    private FormatBoundKeywordHandler(bool isMaximum)
    {
        _isMaximum = isMaximum;
        Name = isMaximum ? MaximumKeyword : MinimumKeyword;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public object ValidateKeywordValue(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException(
                $"'{Name}' must be a string containing an ISO-8601 date or a Sorcha date token " +
                "(e.g. 'today', 'today-18Y').");
        }

        return value.GetString()!;
    }

    /// <inheritdoc />
    public void BuildSubschemas(KeywordData keyword, BuildContext context)
    {
        // A scalar bound — no subschemas to build.
    }

    /// <inheritdoc />
    public KeywordEvaluation Evaluate(KeywordData keyword, EvaluationContext context)
    {
        // Not a string — `type` owns that complaint.
        if (context.Instance.ValueKind != JsonValueKind.String)
        {
            return Inapplicable();
        }

        // Not a date — `format: date` owns that complaint. Reporting it here as well would
        // surface two errors for a single defect.
        if (!TryParseIsoDate(context.Instance.GetString(), out var instanceDate))
        {
            return Inapplicable();
        }

        var token = keyword.Value as string ?? string.Empty;

        DateOnly bound;
        try
        {
            bound = SorchaDateTokenResolver.Resolve(token, DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (FormatException ex)
        {
            // The SCHEMA is wrong, not the data. Fail closed: a cutoff that cannot be resolved
            // must not quietly admit every value.
            return new KeywordEvaluation
            {
                Keyword = Name,
                IsValid = false,
                Error = $"'{Name}' bound could not be resolved: {ex.Message}"
            };
        }

        var satisfied = _isMaximum ? instanceDate <= bound : instanceDate >= bound;
        if (satisfied)
        {
            return new KeywordEvaluation { Keyword = Name, IsValid = true };
        }

        return new KeywordEvaluation
        {
            Keyword = Name,
            IsValid = false,
            Error = _isMaximum
                ? $"Value should be on or before {bound:yyyy-MM-dd} ({MaximumKeyword} '{token}')"
                : $"Value should be on or after {bound:yyyy-MM-dd} ({MinimumKeyword} '{token}')"
        };
    }

    private KeywordEvaluation Inapplicable() => new()
    {
        Keyword = Name,
        IsValid = true,
        ContributesToValidation = false
    };

    private static bool TryParseIsoDate(string? value, out DateOnly date)
    {
        date = default;
        return value is not null
            && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
    }
}
