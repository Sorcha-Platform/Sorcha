// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// Read-side wire shape returned by <c>GET /me/persona</c>. Every scalar
/// attribute is wrapped in a <see cref="PersonaAttribute{T}"/> carrying its
/// provenance metadata. For multi-value attributes, the default entry is
/// exposed via <c>Default*</c> (wrapped) and the full list via <c>All*</c>.
/// </summary>
/// <remarks>
/// A user who has never saved a persona receives a fully empty instance — all
/// scalar fields null, all lists empty. New users are never returned a 404.
/// </remarks>
public sealed record PersonaReadModelV1
{
    /// <summary>Given (first) name, wrapped with provenance.</summary>
    public PersonaAttribute<string>? GivenName { get; init; }

    /// <summary>
    /// Middle name, wrapped with provenance. Null for personas saved before
    /// Feature 103 (Verified Citizen v2) introduced the field, and for users
    /// who haven't set a middle name. Used by the <c>PersonName</c> core
    /// primitive for autofill.
    /// </summary>
    public PersonaAttribute<string>? MiddleName { get; init; }

    /// <summary>Family (last) name, wrapped with provenance.</summary>
    public PersonaAttribute<string>? FamilyName { get; init; }

    /// <summary>Full name fallback, wrapped with provenance.</summary>
    public PersonaAttribute<string>? FullName { get; init; }

    /// <summary>Date of birth, wrapped with provenance.</summary>
    public PersonaAttribute<DateOnly>? DateOfBirth { get; init; }

    /// <summary>
    /// The default email entry wrapped with provenance, or null if no emails
    /// are saved. Used by form autofill.
    /// </summary>
    public PersonaAttribute<PersonaEmail>? DefaultEmail { get; init; }

    /// <summary>All saved email entries.</summary>
    public IReadOnlyList<PersonaEmail> AllEmails { get; init; } = [];

    /// <summary>
    /// The default phone entry wrapped with provenance, or null if no phones
    /// are saved. Used by form autofill.
    /// </summary>
    public PersonaAttribute<PersonaPhone>? DefaultPhone { get; init; }

    /// <summary>All saved phone entries.</summary>
    public IReadOnlyList<PersonaPhone> AllPhones { get; init; } = [];

    /// <summary>
    /// The default address entry wrapped with provenance, or null if no
    /// addresses are saved. Used by form autofill.
    /// </summary>
    public PersonaAttribute<PersonaAddress>? DefaultAddress { get; init; }

    /// <summary>All saved address entries.</summary>
    public IReadOnlyList<PersonaAddress> AllAddresses { get; init; } = [];

    /// <summary>
    /// The default nationality wrapped with provenance, or null if no
    /// nationalities are saved. Used by form autofill.
    /// </summary>
    public PersonaAttribute<string>? DefaultNationality { get; init; }

    /// <summary>All saved nationalities (ISO 3166-1 alpha-2).</summary>
    public IReadOnlyList<string> AllNationalities { get; init; } = [];
}
