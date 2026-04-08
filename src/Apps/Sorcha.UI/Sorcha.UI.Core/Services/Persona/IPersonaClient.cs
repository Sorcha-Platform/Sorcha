// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Persona;

namespace Sorcha.UI.Core.Services.Persona;

/// <summary>
/// HTTP transport contract for the Tenant Service <c>/me/persona</c>
/// endpoints. A thin wrapper that <see cref="IPersonaService"/> composes with
/// a session cache and preference storage.
/// </summary>
public interface IPersonaClient
{
    /// <summary>
    /// Calls <c>GET /api/me/persona</c>. Returns null on non-success so the
    /// caller can distinguish transient failure from an empty persona — an
    /// empty persona is still a 200 with null/empty fields.
    /// </summary>
    Task<PersonaReadModelV1?> GetPersonaAsync(string actingAs = "self", CancellationToken ct = default);

    /// <summary>
    /// Calls <c>PUT /api/me/persona</c>. Returns the server-canonical read
    /// model on success. Throws on 400 (validation) and 409 (wallet not
    /// provisioned) so the caller can surface inline errors.
    /// </summary>
    Task<PersonaReadModelV1> PutPersonaAsync(PersonaAttributesV1 persona, CancellationToken ct = default);

    /// <summary>
    /// Calls <c>DELETE /api/me/persona</c>. Idempotent — 204 whether or not
    /// a row existed.
    /// </summary>
    Task DeletePersonaAsync(CancellationToken ct = default);
}

/// <summary>
/// Thrown when the Tenant Service rejects a persona write with field-level
/// validation errors (code <c>invalid_email</c>, <c>multiple_defaults</c>,
/// etc.). The UI surfaces the <see cref="Errors"/> dictionary inline.
/// </summary>
public sealed class PersonaValidationException : Exception
{
    public PersonaValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Persona validation failed")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// Thrown on write when the user has no primary wallet provisioned. The UI
/// shows a recoverable "Provision a wallet first" message.
/// </summary>
public sealed class PersonaWalletNotProvisionedException : Exception
{
    public PersonaWalletNotProvisionedException()
        : base("A wallet must be provisioned before saving a personal profile.") { }
}
