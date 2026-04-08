// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Persona;

/// <summary>
/// Options for reading a persona. Exists from day one so that the delegation
/// follow-up (Power of Attorney) can add new values to <see cref="ActingAs"/>
/// without a breaking change to the read contract.
/// </summary>
/// <remarks>
/// In v1 the only valid value for <see cref="ActingAs"/> is the literal string
/// <c>"self"</c>. Any other value is rejected with a 400 error code
/// <c>actingAs_not_supported</c>. When wallet delegation lands, this field
/// will accept the delegation grant id or principal user id of the caller's
/// active delegation.
/// </remarks>
public sealed record PersonaReadOptions
{
    /// <summary>
    /// The identity the caller is acting on behalf of. In v1 only
    /// <c>"self"</c> is accepted.
    /// </summary>
    public string ActingAs { get; init; } = "self";
}
