// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// A single postal address entry in a user's persona.
/// </summary>
/// <param name="Line1">Street address line 1.</param>
/// <param name="Line2">Street address line 2 (optional).</param>
/// <param name="City">City / town.</param>
/// <param name="Region">State / province / region (optional — omitted in many locales).</param>
/// <param name="PostalCode">Postal / ZIP code.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code (uppercase).</param>
/// <param name="IsDefault">Whether this entry is the default used for autofill.
/// Exactly one entry per list must be marked as default when the list is
/// non-empty.</param>
/// <param name="Label">An optional human label such as "Home" or "Work".</param>
public sealed record PersonaAddress(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country,
    bool IsDefault,
    string? Label = null);
