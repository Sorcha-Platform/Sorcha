// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Persona;

namespace Sorcha.UI.Core.Services.Persona;

/// <summary>
/// Client-side service for reading and writing the signed-in user's persona
/// and managing the global autofill preference. Wraps the Tenant Service
/// <c>/me/persona</c> endpoints with a session-lifetime in-memory cache so
/// the form renderer does not round-trip on every form open.
/// </summary>
/// <remarks>
/// The <see cref="PersonaReadOptions"/> parameter on <see cref="GetAsync"/>
/// is designed in from day one so that the delegation follow-up (Power of
/// Attorney) can add new values to <c>ActingAs</c> without a breaking change.
/// In v1 only <c>"self"</c> is accepted.
/// </remarks>
public interface IPersonaService
{
    /// <summary>
    /// Gets the signed-in user's persona. Returns an empty
    /// <see cref="PersonaReadModelV1"/> (not null) when the user has no
    /// saved persona yet. Uses an in-memory cache for the current session.
    /// </summary>
    Task<PersonaReadModelV1> GetAsync(
        PersonaReadOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the persona with the supplied plaintext. Invalidates the
    /// session cache on success.
    /// </summary>
    Task<PersonaReadModelV1> UpdateAsync(
        PersonaAttributesV1 persona,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the persona. Idempotent.
    /// </summary>
    Task DeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the global "autofill forms from my profile" preference. Defaults
    /// to <c>true</c> when the user has no stored value.
    /// </summary>
    Task<bool> GetAutofillEnabledAsync();

    /// <summary>
    /// Sets the global autofill preference.
    /// </summary>
    Task SetAutofillEnabledAsync(bool enabled);

    /// <summary>
    /// Explicitly clears the session cache. Called by the logout and
    /// org-switch flows so a subsequent login does not see stale data.
    /// </summary>
    void InvalidateCache();
}
