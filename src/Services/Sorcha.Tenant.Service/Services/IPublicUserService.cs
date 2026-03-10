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

    /// <summary>
    /// Counts the total number of active authentication methods for a public user.
    /// Includes active passkey credentials and social login links.
    /// </summary>
    /// <param name="publicIdentityId">The public identity ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The total count of active auth methods.</returns>
    Task<int> GetAuthMethodCountAsync(
        Guid publicIdentityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a social login link from a public user. Enforces last-auth-method guard:
    /// if the user has only one remaining auth method (passkey or social link), the
    /// removal is rejected to prevent account lockout.
    /// </summary>
    /// <param name="publicIdentityId">The public identity ID.</param>
    /// <param name="linkId">The social login link ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success, not-found, or last-method-guard rejection.</returns>
    Task<RemoveAuthMethodResult> RemoveSocialLinkAsync(
        Guid publicIdentityId,
        Guid linkId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of attempting to remove an auth method (social link or passkey).
/// </summary>
/// <param name="Success">Whether the removal succeeded.</param>
/// <param name="Error">Error message if the removal failed.</param>
/// <param name="IsLastMethodGuard">True if removal was blocked because it's the user's last auth method.</param>
public record RemoveAuthMethodResult(bool Success, string? Error = null, bool IsLastMethodGuard = false)
{
    /// <summary>
    /// The removal succeeded.
    /// </summary>
    public static RemoveAuthMethodResult Succeeded { get; } = new(true);

    /// <summary>
    /// The link was not found for this user.
    /// </summary>
    public static RemoveAuthMethodResult NotFound { get; } = new(false, "Social login link not found.");

    /// <summary>
    /// Cannot remove the last auth method — would lock the user out.
    /// </summary>
    public static RemoveAuthMethodResult LastMethod { get; } = new(false, "Cannot remove the last authentication method.", true);
}
