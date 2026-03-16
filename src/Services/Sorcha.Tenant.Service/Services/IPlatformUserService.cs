// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a password validation attempt against a PlatformUser.
/// </summary>
/// <param name="Success">Whether the password was correct and the account is not locked.</param>
/// <param name="IsLocked">Whether the account is currently locked (temporary or permanent).</param>
/// <param name="IsPermanentlyLocked">Whether the account requires administrator unlock.</param>
/// <param name="LockedUntil">When the temporary lock expires, if applicable.</param>
public record PasswordAuthResult(
    bool Success,
    bool IsLocked = false,
    bool IsPermanentlyLocked = false,
    DateTimeOffset? LockedUntil = null);

/// <summary>
/// Service interface for platform-wide user management operations.
/// Handles PlatformUser lifecycle, social login linking, and organisation membership.
/// </summary>
public interface IPlatformUserService
{
    /// <summary>
    /// Creates a new platform user with the specified credentials.
    /// </summary>
    /// <param name="email">Platform-wide unique email address.</param>
    /// <param name="displayName">Default display name for the user.</param>
    /// <param name="passwordHash">BCrypt password hash, or null for social-login-only users.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created platform user.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a user with the same email already exists.</exception>
    Task<PlatformUser> CreateAsync(string email, string displayName, string? passwordHash, CancellationToken ct);

    /// <summary>
    /// Gets a platform user by their unique identifier.
    /// </summary>
    /// <param name="id">The platform user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The platform user, or null if not found.</returns>
    Task<PlatformUser?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Gets a platform user by their email address (case-insensitive).
    /// </summary>
    /// <param name="email">The email address to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The platform user, or null if not found.</returns>
    Task<PlatformUser?> GetByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Gets a platform user by their social login provider and subject identifier.
    /// </summary>
    /// <param name="provider">Social provider name (e.g., "google", "github").</param>
    /// <param name="subject">Provider's unique user identifier (the "sub" claim).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The platform user linked to the provider/subject, or null if not found.</returns>
    Task<PlatformUser?> GetByProviderSubjectAsync(string provider, string subject, CancellationToken ct);

    /// <summary>
    /// Updates an existing platform user entity.
    /// </summary>
    /// <param name="user">The platform user with updated values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(PlatformUser user, CancellationToken ct);

    /// <summary>
    /// Links a social login provider to an existing platform user.
    /// </summary>
    /// <param name="platformUserId">The platform user ID to link the social login to.</param>
    /// <param name="provider">Social provider name (e.g., "google", "github").</param>
    /// <param name="subject">Provider's unique user identifier.</param>
    /// <param name="email">Email address from the social provider profile.</param>
    /// <param name="displayName">Display name from the social provider profile.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created social login link.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the provider/subject combination is already linked.</exception>
    Task<PlatformSocialLogin> LinkSocialLoginAsync(Guid platformUserId, string provider, string subject, string? email, string? displayName, CancellationToken ct);

    /// <summary>
    /// Gets all organisation memberships for a platform user.
    /// </summary>
    /// <param name="platformUserId">The platform user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of the user's organisation memberships.</returns>
    Task<IReadOnlyList<PlatformUserOrgMembership>> GetOrgMembershipsAsync(Guid platformUserId, CancellationToken ct);

    /// <summary>
    /// Adds an organisation membership for a platform user.
    /// </summary>
    /// <param name="platformUserId">The platform user ID.</param>
    /// <param name="organizationId">The organisation ID to join.</param>
    /// <param name="role">The role to assign in the organisation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created organisation membership.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the user already has a membership in the organisation.</exception>
    Task<PlatformUserOrgMembership> AddOrgMembershipAsync(Guid platformUserId, Guid organizationId, string role, CancellationToken ct);

    /// <summary>
    /// Validates a password against a PlatformUser's stored hash with progressive lockout.
    /// Checks lockout state before verification; increments FailedLoginCount on failure;
    /// resets on success. Lockout thresholds: 5→15min, 10→30min, 15→1hr, 20→4hr, 25+→permanent.
    /// </summary>
    /// <param name="platformUser">The platform user to authenticate.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success, lockout, or invalid credentials.</returns>
    Task<PasswordAuthResult> ValidatePasswordAsync(PlatformUser platformUser, string password, CancellationToken ct);

    /// <summary>
    /// Resolves or creates a PlatformUser from social login claims.
    /// Resolution order: (1) find by provider+subject, (2) find by email and link provider, (3) create new user + link.
    /// </summary>
    /// <param name="provider">Social provider name (e.g., "google", "github").</param>
    /// <param name="subject">Provider's unique user identifier.</param>
    /// <param name="email">Email address from the social provider.</param>
    /// <param name="displayName">Display name from the social provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved or created platform user, and whether it was newly created.</returns>
    Task<(PlatformUser User, bool IsNew)> ResolveOrCreateSocialUserAsync(
        string provider, string subject, string? email, string? displayName, CancellationToken ct);
}
