// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Persona;

namespace Sorcha.UI.Core.Models.Persona;

/// <summary>
/// Turns a persona attribute's provenance into citizen-facing words.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1271 (UT-008): <see cref="PersonaAttribute{T}.Source"/> and
/// <see cref="PersonaAttribute{T}.VerifiedBy"/> are carried all the way to the UI and then dropped —
/// nothing rendered them, so a citizen could not tell a value they had typed themselves from one
/// backed by a credential.
/// </para>
/// <para>
/// That gap is not cosmetic. UT-001 was a wrongful rejection caused by exactly this confusion
/// between two different notions of "verified": a self-asserted persona value and a verified account
/// claim. The wording is deliberately blunt — "You entered this" makes no claim of verification, and
/// an unrecognised source falls back to it, because over-claiming verification is the dangerous
/// direction.
/// </para>
/// </remarks>
public static class PersonaProvenance
{
    /// <summary>Short provenance label, e.g. "You entered this" or "Verified by did:sorcha:aias".</summary>
    public static string Describe(PersonaAttributeSource source, string? verifiedBy) =>
        source switch
        {
            PersonaAttributeSource.VerifiedCredential when !string.IsNullOrWhiteSpace(verifiedBy)
                => $"Verified by {verifiedBy}",
            PersonaAttributeSource.VerifiedCredential => "Verified by a credential",
            _ => "You entered this",
        };
}
