// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// A single email address entry in a user's persona.
/// </summary>
/// <param name="Value">The email address in RFC 5322 basic shape.</param>
/// <param name="IsDefault">Whether this entry is the default used for autofill.
/// Exactly one entry per list must be marked as default when the list is
/// non-empty.</param>
/// <param name="Label">An optional human label such as "Personal" or "Work".</param>
public sealed record PersonaEmail(
    string Value,
    bool IsDefault,
    string? Label = null);
