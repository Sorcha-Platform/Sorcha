// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Persona;

namespace Sorcha.UI.Core.Models.Forms;

/// <summary>
/// How a form field was matched to a persona attribute by
/// <c>PersonaAutofillResolver</c>. Feature 092.
/// </summary>
public enum PersonaMatchMode
{
    /// <summary>
    /// The field schema carried an explicit <c>x-persona</c> extension that
    /// named a persona attribute (e.g. <c>"defaultEmail"</c>).
    /// </summary>
    ExplicitExtension = 0,

    /// <summary>
    /// The field was matched via the conservative inference allowlist
    /// (<c>format: email|tel</c>, known date-of-birth field names, postal
    /// address schema shape). See research.md Decision 5.
    /// </summary>
    Inference = 1
}

/// <summary>
/// The outcome of resolving a single form field against the signed-in user's
/// persona. One instance per field the resolver decided to fill.
/// </summary>
/// <param name="FieldPath">
/// JSON Pointer of the target field relative to the form data root
/// (e.g. <c>"/email"</c>, <c>"/address/postalCode"</c>).
/// </param>
/// <param name="AttributeName">
/// Canonical name of the persona attribute that supplied the value. Matches
/// property names on <see cref="PersonaReadModelV1"/> using camelCase
/// (e.g. <c>"givenName"</c>, <c>"defaultEmail"</c>, <c>"dateOfBirth"</c>).
/// </param>
/// <param name="Value">
/// The value to write into the form data bag. Already unwrapped from the
/// <see cref="PersonaAttribute{T}"/> envelope.
/// </param>
/// <param name="Source">
/// Provenance of the persona attribute at resolution time. Copied from
/// <see cref="PersonaAttribute{T}.Source"/>.
/// </param>
/// <param name="MatchedBy">
/// Whether the match came from an explicit <c>x-persona</c> schema extension
/// or from the inference allowlist.
/// </param>
public sealed record PersonaFillResult(
    string FieldPath,
    string AttributeName,
    object? Value,
    PersonaAttributeSource Source,
    PersonaMatchMode MatchedBy);
