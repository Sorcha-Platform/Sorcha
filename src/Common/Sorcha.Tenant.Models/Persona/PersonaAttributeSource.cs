// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// Indicates the provenance of a persona attribute value.
/// </summary>
/// <remarks>
/// In v1 all persona attributes are <see cref="SelfAsserted"/>. The
/// <see cref="VerifiedCredential"/> value is reserved for a future feature that
/// will back persona attributes with verifiable credentials — it is part of
/// the contract today so the read shape is stable across that upgrade.
/// </remarks>
public enum PersonaAttributeSource
{
    /// <summary>
    /// The value was typed by the user into their profile.
    /// </summary>
    SelfAsserted = 0,

    /// <summary>
    /// The value is backed by a verifiable credential held by the user.
    /// Reserved for a future feature; never returned in v1.
    /// </summary>
    VerifiedCredential = 1,
}
