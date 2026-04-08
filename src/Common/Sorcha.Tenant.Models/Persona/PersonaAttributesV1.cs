// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// Plaintext write-side shape of a user's persona. This is the JSON payload
/// the client sends to <c>PUT /me/persona</c> and the intermediate shape that
/// is encrypted before being stored in Tenant Service. Never exposed over the
/// wire on the read path — reads return <see cref="PersonaReadModelV1"/>.
/// </summary>
/// <remarks>
/// Invariants (enforced by the Tenant PersonaService on write):
/// <list type="bullet">
///   <item>Each multi-value list (Emails, Phones, Addresses, Nationalities) is
///     capped at 5 entries.</item>
///   <item>If a list is non-empty, exactly one entry must be marked
///     <c>IsDefault = true</c>. If none are marked the service promotes the
///     first entry. If more than one are marked the write is rejected.</item>
///   <item>Email values must match RFC 5322 basic shape.</item>
///   <item>Phone values must be E.164.</item>
///   <item>Country codes and nationalities must be ISO 3166-1 alpha-2.</item>
/// </list>
/// </remarks>
public sealed record PersonaAttributesV1
{
    /// <summary>Given (first) name.</summary>
    public string? GivenName { get; init; }

    /// <summary>Family (last) name.</summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Full name fallback — used by the read-side resolver only when both
    /// <see cref="GivenName"/> and <see cref="FamilyName"/> are null. Providing
    /// all three is permitted.
    /// </summary>
    public string? FullName { get; init; }

    /// <summary>Date of birth (ISO 8601).</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Email addresses (0..5 entries, exactly one default if non-empty).</summary>
    public IReadOnlyList<PersonaEmail> Emails { get; init; } = [];

    /// <summary>Phone numbers (0..5 entries, exactly one default if non-empty).</summary>
    public IReadOnlyList<PersonaPhone> Phones { get; init; } = [];

    /// <summary>Postal addresses (0..5 entries, exactly one default if non-empty).</summary>
    public IReadOnlyList<PersonaAddress> Addresses { get; init; } = [];

    /// <summary>Nationalities (0..5 entries, ISO 3166-1 alpha-2).</summary>
    public IReadOnlyList<string> Nationalities { get; init; } = [];
}
