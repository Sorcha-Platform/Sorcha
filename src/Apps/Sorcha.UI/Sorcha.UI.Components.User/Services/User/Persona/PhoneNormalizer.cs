// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;

namespace Sorcha.UI.Core.Services.Persona;

/// <summary>
/// Best-effort conversion of a user-typed phone number to E.164, shared by every persona surface
/// (onboarding profile step and the My Profile editor) so they behave identically. Strips
/// spaces/dashes/brackets, maps a leading international access code ("00…" → "+…"), and promotes a
/// national trunk-prefixed number ("07966 242717" in GB → "+447966242717") using the browser's own
/// region. If the region can't be resolved to a known calling code the national number is left as
/// typed — a wrong country code is never invented; validation then prompts for the international form.
/// </summary>
public static class PhoneNormalizer
{
    /// <summary>Returns the E.164 form of <paramref name="raw"/>, or null when it is blank.</summary>
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (string.IsNullOrEmpty(cleaned)) return null;

        if (cleaned.StartsWith('+')) return cleaned;
        if (cleaned.StartsWith("00")) return "+" + cleaned[2..];
        if (cleaned.StartsWith('0'))
        {
            var callingCode = LocalCallingCode();
            if (callingCode is not null) return "+" + callingCode + cleaned[1..];
        }

        return cleaned;
    }

    private static string? LocalCallingCode()
    {
        try
        {
            // Derive the region from the current culture (Blazor WASM sets it from navigator.language,
            // e.g. "en-GB") rather than RegionInfo.CurrentRegion, which reads the OS region. A
            // region-less culture (e.g. "en") makes RegionInfo throw → null → leave the number as typed.
            var region = new RegionInfo(CultureInfo.CurrentCulture.Name);
            return CallingCodesByRegion.GetValueOrDefault(region.TwoLetterISORegionName);
        }
        catch
        {
            return null;
        }
    }

    // Common trunk-"0" regions only — an unmapped region falls through to "leave the number as typed"
    // rather than risk prepending the wrong country code. US/CA (+1) have no trunk 0, so their
    // national numbers never start with 0 and don't need an entry.
    private static readonly Dictionary<string, string> CallingCodesByRegion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = "44",  ["IE"] = "353", ["AU"] = "61", ["NZ"] = "64", ["FR"] = "33", ["DE"] = "49",
        ["ES"] = "34",  ["IT"] = "39",  ["NL"] = "31", ["BE"] = "32", ["PT"] = "351", ["SE"] = "46",
        ["NO"] = "47",  ["DK"] = "45",  ["FI"] = "358", ["CH"] = "41", ["AT"] = "43", ["PL"] = "48",
        ["IN"] = "91",  ["ZA"] = "27",
    };

    /// <summary>
    /// Maps a persona validation error (field pointer + code) to a human-readable message, so surfaces
    /// show "Phone number: use international format…" rather than the raw "phones[0].value: invalid_phone".
    /// </summary>
    public static string DescribeError(string field, string code) => code switch
    {
        "invalid_phone" => "Phone number: enter it in international format, e.g. +447700900123 — or leave it blank.",
        "invalid_email" => "That email address doesn't look valid. Please check it.",
        "invalid_country_code" => $"{PrettyField(field)}: use a 2-letter country code (e.g. GB).",
        "required" => $"{PrettyField(field)} is required.",
        "list_cap_exceeded" => $"{PrettyField(field)}: too many entries (maximum 5).",
        "multiple_defaults" => $"{PrettyField(field)}: only one entry can be the default.",
        _ => $"{PrettyField(field)}: {code}",
    };

    private static string PrettyField(string field)
    {
        if (field.StartsWith("phone", StringComparison.OrdinalIgnoreCase)) return "Phone number";
        if (field.StartsWith("email", StringComparison.OrdinalIgnoreCase)) return "Email address";
        if (field.StartsWith("address", StringComparison.OrdinalIgnoreCase)) return "Address";
        if (field.StartsWith("nationalit", StringComparison.OrdinalIgnoreCase)) return "Nationality";
        return "One of the details you entered";
    }
}
