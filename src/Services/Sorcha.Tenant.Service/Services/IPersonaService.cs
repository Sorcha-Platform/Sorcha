// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Persona;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Orchestrates persona read / write operations for a platform user.
/// Delegates crypto to the Wallet Service via <see cref="IPersonaCryptoClient"/>
/// and stores the resulting ciphertext in the Tenant database.
/// </summary>
public interface IPersonaService
{
    /// <summary>
    /// Loads the persona for a platform user under a given organisational
    /// context and returns the read-side wire shape. A user who has never
    /// saved a persona for the requested context receives an empty
    /// <see cref="PersonaReadModelV1"/> (all fields null / empty lists) —
    /// never a "not found" error.
    /// </summary>
    /// <param name="platformUserId">Cross-org PlatformUser id from the caller's JWT.</param>
    /// <param name="contextOrgId">Organisation context for the persona; null = Personal (Feature 125).</param>
    /// <param name="options">Read options (e.g. <c>actingAs</c>); v1 only supports <c>self</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PersonaReadModelV1> GetAsync(
        Guid platformUserId,
        Guid? contextOrgId = null,
        PersonaReadOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the persona for a platform user with the supplied plaintext.
    /// Validates invariants, encrypts via the Wallet Service using
    /// <paramref name="walletAddress"/>, upserts the row and writes an
    /// activity log entry.
    /// </summary>
    /// <param name="platformUserId">The cross-org <see cref="PlatformUser"/> id
    /// that owns the persona row (FK target). This is the <c>platform_user_id</c>
    /// claim on the caller's JWT, not <c>sub</c> — <c>sub</c> carries the
    /// org-scoped <c>UserIdentity.Id</c>.</param>
    /// <param name="walletAddress">Wallet address used to derive the content
    /// key via the Wallet Service. Stored in <c>WrappedKeyRef</c> so reads
    /// can re-derive the same key on decrypt. Passed in from the caller's
    /// <c>wallet_address</c> JWT claim; <see cref="PersonaWalletNotProvisionedException"/>
    /// is thrown if null or empty.</param>
    /// <param name="plaintext">The persona attributes to encrypt and persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="PersonaValidationException">Invariant violation — invalid
    /// email / phone / country code, multiple defaults, or list cap exceeded.</exception>
    /// <exception cref="PersonaWalletNotProvisionedException">No wallet address
    /// supplied — cannot encrypt.</exception>
    Task<PersonaReadModelV1> ReplaceAsync(
        Guid platformUserId,
        string? walletAddress,
        PersonaAttributesV1 plaintext,
        Guid? contextOrgId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes the persona for a platform user under the given context.
    /// Idempotent — returns silently whether or not a row existed.
    /// <paramref name="contextOrgId"/> null means the Personal context (Feature 125).
    /// </summary>
    Task DeleteAsync(
        Guid platformUserId,
        Guid? contextOrgId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Thrown when a persona write violates an invariant. The
/// <see cref="Errors"/> dictionary maps field paths to user-facing codes
/// (<c>multiple_defaults</c>, <c>list_cap_exceeded</c>, <c>invalid_email</c>,
/// <c>invalid_phone</c>, <c>invalid_country_code</c>).
/// </summary>
public sealed class PersonaValidationException : Exception
{
    /// <summary>
    /// Creates the exception with a map of field path → error codes.
    /// </summary>
    public PersonaValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Persona validation failed")
    {
        Errors = errors;
    }

    /// <summary>
    /// Field-keyed error codes. Matches the client-side
    /// <c>PersonaValidationException.Errors</c> shape so both sides can
    /// handle the validation problem response consistently.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// Thrown on write when the user has no primary wallet provisioned. Writes
/// require a wallet because the encryption key is derived from wallet key
/// material. Reads succeed with an empty persona instead of throwing.
/// </summary>
public sealed class PersonaWalletNotProvisionedException : Exception
{
    public PersonaWalletNotProvisionedException()
        : base("A wallet must be provisioned before saving a personal profile.")
    { }
}
