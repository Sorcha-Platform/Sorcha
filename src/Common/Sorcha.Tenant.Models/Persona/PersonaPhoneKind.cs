// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// The kind of a <see cref="PersonaPhone"/> entry. Optional — users may omit.
/// </summary>
public enum PersonaPhoneKind
{
    /// <summary>Mobile / cellular number.</summary>
    Mobile = 0,

    /// <summary>Home landline.</summary>
    Home = 1,

    /// <summary>Work number.</summary>
    Work = 2,
}
