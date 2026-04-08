// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// A single phone number entry in a user's persona.
/// </summary>
/// <param name="Value">The phone number in E.164 format (e.g. +353871234567).</param>
/// <param name="IsDefault">Whether this entry is the default used for autofill.
/// Exactly one entry per list must be marked as default when the list is
/// non-empty.</param>
/// <param name="Label">An optional human label such as "Personal" or "Work".</param>
/// <param name="Kind">An optional classification — mobile, home, or work.</param>
public sealed record PersonaPhone(
    string Value,
    bool IsDefault,
    string? Label = null,
    PersonaPhoneKind? Kind = null);
