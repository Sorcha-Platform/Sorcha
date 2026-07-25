// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// Write-path normalisation for address values, applied BEFORE the field is validated.
/// </summary>
/// <remarks>
/// Issue #1278: <see cref="TextInputHints"/> stops a phone corrupting a value as it is typed, but a
/// hint is advisory — it cannot fix a value that was pasted, dictated, or typed on a keyboard that
/// ignored it. Normalising before validation is what actually guarantees what gets signed, and it
/// also means the citizen never sees a validation error for a postcode that was only ever wrong in
/// its casing.
/// </remarks>
public static class PostalValueNormaliser
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Trims, collapses internal whitespace, and upper-cases. Postal codes are case-insensitive
    /// identifiers whose canonical form is upper case in every scheme Sorcha meets, so this is
    /// lossless — "Eh91ja" and "EH91JA" are the same postcode, and only one of them is writable.
    /// </summary>
    public static string Postcode(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : Whitespace.Replace(raw.Trim(), " ").ToUpperInvariant();

    /// <summary>
    /// Trims and collapses internal whitespace. Case is deliberately left alone: a country name is
    /// prose, so sentence capitalisation is CORRECT for it — and upper-casing "United Kingdom" would
    /// be a regression dressed up as consistency with the postcode rule.
    /// </summary>
    public static string Country(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : Whitespace.Replace(raw.Trim(), " ");
}
