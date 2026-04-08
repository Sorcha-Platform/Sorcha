// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// A read-side wrapper that carries a persona attribute value together with
/// its provenance metadata. Used in <see cref="PersonaReadModelV1"/>.
/// </summary>
/// <typeparam name="T">The attribute value type (e.g. <see cref="string"/>,
/// <see cref="DateOnly"/>, <see cref="PersonaEmail"/>).</typeparam>
/// <param name="Value">The attribute value.</param>
/// <param name="Source">Where the value came from — self-asserted in v1.</param>
/// <param name="VerifiedBy">The DID of the issuer if the value is backed by a
/// verifiable credential; always null in v1.</param>
/// <param name="LastUpdated">When the attribute was last written. Uses
/// <see cref="DateTimeOffset"/> so the wire contract carries zone information
/// and survives a PostgreSQL <c>timestamptz</c> round-trip without ambiguity.</param>
public sealed record PersonaAttribute<T>(
    T Value,
    PersonaAttributeSource Source,
    string? VerifiedBy,
    DateTimeOffset LastUpdated);
