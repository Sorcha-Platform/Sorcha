// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Keyboard-behaviour hints for a blueprint-driven text input, derived from the field's own schema.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1278: no form text input set <c>autocapitalize</c>, so a mobile keyboard auto-capitalised
/// what the citizen typed. The n1 agent log shows <c>EH91JA</c> on submission 1 and <c>Eh91ja</c> on
/// submission 2 — the value ARRIVED corrupted. (It is not a render transform: there is no
/// <c>text-transform: capitalize</c> anywhere in src/, and capitalize cannot lowercase a tail.)
/// </para>
/// <para>
/// The rule is deliberately drawn around whether the value is MACHINE-CHECKED rather than around a
/// list of field names. A pattern, a machine <c>format</c>, an <c>enum</c> or an address lookup all
/// say "a machine will parse this", and there a keyboard's guess can only break a value the server
/// then rejects — or, worse, silently stores wrong. Everything else is prose, where capitalising the
/// first letter of a sentence is exactly what the citizen wants; so prose emits NO attributes and
/// leaves the browser alone.
/// </para>
/// <para>
/// Hints are advisory — a keyboard may ignore them. They are the first half of the fix; the write
/// path (<see cref="PostalValueNormaliser"/>, applied before validation) is what actually guarantees
/// what gets signed.
/// </para>
/// </remarks>
/// <param name="AutoCapitalize">HTML <c>autocapitalize</c>, or null to leave the browser default.</param>
/// <param name="AutoCorrect">HTML <c>autocorrect</c>, or null to leave the browser default.</param>
/// <param name="SpellCheck">HTML <c>spellcheck</c>, or null to leave the browser default.</param>
/// <param name="InputMode">HTML <c>inputmode</c>, or null to make no keyboard claim.</param>
public sealed record TextInputHints(
    string? AutoCapitalize,
    string? AutoCorrect,
    string? SpellCheck,
    string? InputMode)
{
    /// <summary>Free text. Emits nothing — the browser defaults are correct for prose.</summary>
    public static TextInputHints Prose { get; } = new(null, null, null, null);

    /// <summary>A machine-checked value: no capitalisation, no autocorrect, no spellcheck.</summary>
    public static TextInputHints ExactValue { get; } = new("none", "off", "false", null);

    /// <summary>An email address — exact, and worth asking for the email keyboard.</summary>
    public static TextInputHints Email { get; } = new("none", "off", "false", "email");

    /// <summary>
    /// JSON Schema <c>format</c> values whose payload a machine parses. Prose formats are
    /// deliberately absent, and an unrecognised format falls through to <see cref="Prose"/> rather
    /// than being guessed at.
    /// </summary>
    private static readonly HashSet<string> MachineFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "uri", "uri-reference", "iri", "url", "uuid", "hostname", "idn-hostname",
        "ipv4", "ipv6", "date", "date-time", "time", "duration", "regex",
        "json-pointer", "postal-code",
    };

    /// <summary>Derives the hints for a field from its schema. A null schema yields <see cref="Prose"/>.</summary>
    public static TextInputHints ForField(JsonElement? fieldSchema)
    {
        if (fieldSchema is not { ValueKind: JsonValueKind.Object } schema) return Prose;

        if (schema.TryGetProperty("format", out var formatEl)
            && formatEl.ValueKind == JsonValueKind.String
            && formatEl.GetString() is { Length: > 0 } format)
        {
            if (format.Equals("email", StringComparison.OrdinalIgnoreCase)
                || format.Equals("idn-email", StringComparison.OrdinalIgnoreCase))
            {
                return Email;
            }

            if (MachineFormats.Contains(format)) return ExactValue;
        }

        // Feature 103's address-lookup marker. A postcode is the canonical case: the value is
        // checked against a real gazetteer, and "Eh91ja" is not a postcode.
        if (schema.TryGetProperty("x-address-lookup", out var lookupEl)
            && lookupEl.ValueKind is JsonValueKind.True)
        {
            return ExactValue;
        }

        if (schema.TryGetProperty("pattern", out var patternEl)
            && patternEl.ValueKind == JsonValueKind.String)
        {
            return ExactValue;
        }

        if (schema.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
        {
            return ExactValue;
        }

        return Prose;
    }

    /// <summary>
    /// Renders the hints as a splattable attribute dictionary. Null hints are omitted entirely so a
    /// prose field's markup is byte-identical to what it was before this existed.
    /// </summary>
    public Dictionary<string, object> ToAttributes()
    {
        var attrs = new Dictionary<string, object>(StringComparer.Ordinal);
        if (AutoCapitalize is not null) attrs["autocapitalize"] = AutoCapitalize;
        if (AutoCorrect is not null) attrs["autocorrect"] = AutoCorrect;
        if (SpellCheck is not null) attrs["spellcheck"] = SpellCheck;

        // `inputmode` MUST be supplied as MudBlazor's typed InputMode, never as a string.
        //
        // These attributes are SPLATTED onto a MudTextField, and Blazor matches splatted attribute
        // names to component parameters CASE-INSENSITIVELY. MudTextField has a parameter named
        // InputMode of type MudBlazor.InputMode, so "inputmode" binds to it rather than passing
        // through as a plain HTML attribute — and a string value then throws
        //   InvalidOperationException: Unable to set property 'inputmode' ... Arg_InvalidCastException
        // which the Blazor ErrorBoundary turns into "Something went wrong".
        //
        // Email is the only preset that sets InputMode, so the practical effect was that EVERY form
        // containing an email field crashed the moment that field rendered. It survived because the
        // rehearsal and walkthroughs submit through the API; only the human path renders the field.
        // The other three hints are plain HTML attributes with no MudTextField parameter of the same
        // name, so they splat through untouched and stay strings.
        if (InputMode is not null
            && Enum.TryParse<MudBlazor.InputMode>(InputMode, ignoreCase: true, out var typedInputMode))
        {
            attrs["inputmode"] = typedInputMode;
        }

        return attrs;
    }
}
