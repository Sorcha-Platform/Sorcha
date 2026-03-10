// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a public user creation operation.
/// </summary>
/// <param name="Identity">The created or existing public identity, or null on failure.</param>
/// <param name="IsNewUser">True if a new user was created; false if existing user was returned.</param>
/// <param name="ConflictReason">Non-null when the operation failed due to a conflict (e.g., duplicate email).</param>
public record PublicUserResult(PublicIdentity? Identity, bool IsNewUser, string? ConflictReason = null)
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success => Identity is not null && ConflictReason is null;
}

/// <summary>
/// Service interface for public user (non-organizational) account management.
/// Handles creation and lookup of <see cref="PublicIdentity"/> users who authenticate
/// via passkeys or social login providers without belonging to an organization.
/// </summary>
public interface IPublicUserService
{
    /// <summary>
    /// Creates a new public user from a passkey registration.
    /// Links the provided <see cref="PasskeyCredential"/> to the new identity.
    /// Returns a conflict result (without throwing) if the email is already in use.
    /// </summary>
    /// <param name="displayName">Human-readable display name for the user.</param>
    /// <param name="email">Optional email address. If provided and already in use, returns conflict.</param>
    /// <param name="credential">The verified passkey credential to link to the new identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the created identity or a conflict reason.</returns>
    Task<PublicUserResult> CreatePublicUserAsync(
        string displayName,
        string? email,
        PasskeyCredential credential,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new public user from a social login callback, or links the social account
    /// to an existing user if the email already exists. Returns <see cref="PublicUserResult"/>
    /// with <c>IsNewUser=true</c> for new accounts and <c>IsNewUser=false</c> for linked accounts.
    /// </summary>
    /// <param name="displayName">Human-readable display name from the social provider.</param>
    /// <param name="email">Email address from the social provider.</param>
    /// <param name="socialLoginLink">The social login link to associate with the identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the created identity or a conflict reason.</returns>
    Task<PublicUserResult> CreatePublicUserFromSocialAsync(
        string displayName,
        string? email,
        SocialLoginLink socialLoginLink,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a public user by their unique identifier.
    /// </summary>
    /// <param name="id">The public identity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public identity if found; null otherwise.</returns>
    Task<PublicIdentity?> GetPublicUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a public user by their email address.
    /// </summary>
    /// <param name="email">The email address to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public identity if found; null otherwise.</returns>
    Task<PublicIdentity?> GetPublicUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a public user by a passkey credential ID.
    /// </summary>
    /// <param name="credentialId">The raw credential ID bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public identity if found; null otherwise.</returns>
    Task<PublicIdentity?> GetPublicUserByCredentialIdAsync(
        byte[] credentialId,
        CancellationToken cancellationToken = default);
}
