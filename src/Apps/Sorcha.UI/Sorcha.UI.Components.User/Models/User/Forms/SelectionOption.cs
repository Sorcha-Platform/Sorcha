// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// One choice in a selection control: the <see cref="Value"/> that gets stored and signed, and the
/// <see cref="Label"/> the citizen reads.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed, a selection's stored value and its on-screen text were the same string, so an
/// author had to choose between a stable identifier the citizen cannot read (<c>GB</c>) and readable
/// prose that is not a stable identifier (<c>United Kingdom</c>). Neither is right for reference
/// data, and the second is actively fragile: the AIAS cyber questionnaire scores answers by matching
/// the submitted string against a table, so re-wording a choice silently scores it zero — no error,
/// no log beyond the routine breakdown.
/// </para>
/// <para>
/// Labels are carried in <c>x-enumNames</c>, parallel to <c>enum</c>. That keeps <c>enum</c> intact
/// as the validation contract (the Validator checks payloads against it server-side) and follows the
/// <c>x-*</c> extension convention already used across the platform.
/// </para>
/// </remarks>
/// <param name="Value">The value stored in the payload and covered by the signature.</param>
/// <param name="Label">The text shown to the citizen. Falls back to <see cref="Value"/>.</param>
public sealed record SelectionOption(string Value, string Label)
{
    /// <summary>An option whose label is its value — the shape every selection had before labels.</summary>
    public static SelectionOption Plain(string value) => new(value, value);

    /// <summary>
    /// Reads the options from a field schema: <c>enum</c> for values, optional <c>x-enumNames</c>
    /// for labels.
    /// </summary>
    /// <remarks>
    /// <b>Labels are applied only when the two arrays align exactly.</b> A mismatched
    /// <c>x-enumNames</c> is ignored wholesale rather than zipped as far as it goes, because
    /// partial pairing is the dangerous failure: the citizen picks the option reading
    /// "United Kingdom" and the payload stores <c>FR</c>. Falling back to raw values is ugly and
    /// obvious; mispairing is invisible and wrong, and it is the signed value that carries it.
    /// </remarks>
    public static List<SelectionOption> FromSchema(JsonElement? fieldSchema)
    {
        if (fieldSchema is not { ValueKind: JsonValueKind.Object } schema) return [];

        if (!schema.TryGetProperty("enum", out var enumEl) || enumEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = enumEl.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? e.GetRawText() : e.GetRawText())
            .ToList();

        if (!schema.TryGetProperty("x-enumNames", out var namesEl)
            || namesEl.ValueKind != JsonValueKind.Array)
        {
            return values.Select(Plain).ToList();
        }

        var labels = namesEl.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
            .ToList();

        if (labels.Count != values.Count || labels.Any(string.IsNullOrWhiteSpace))
        {
            return values.Select(Plain).ToList();
        }

        return values.Zip(labels, (v, l) => new SelectionOption(v, l!)).ToList();
    }
}
