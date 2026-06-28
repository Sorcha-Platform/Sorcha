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
/// Reasons a social-login resolve attempt may be refused under the strict
/// link policy introduced in feature 115. The callback page consumes this
/// to render the matching user-facing copy without string matching.
/// </summary>
public enum SocialLoginRefusal
{
    /// <summary>No refusal — the resolve produced a real <see cref="PlatformUser"/>.</summary>
    None = 0,

    /// <summary>The provider asserted <c>email_verified=false</c> (or omitted the claim). Refused per FR-010.</summary>
    ProviderUnverified,

    /// <summary>The email matches an existing Sorcha account whose own <c>EmailVerified</c> is <c>false</c>. Refused per FR-011.</summary>
    ExistingUnverified,

    /// <summary>No existing account matched and creation was disabled (login-only surface, e.g. the citizen wallet PWA). No PlatformUser is created.</summary>
    NoExistingAccount = 3,
}

/// <summary>
/// Result of <see cref="IPlatformUserService.ResolveOrCreateSocialUserAsync"/>.
/// Either <paramref name="User"/> is non-null (success), or
/// <paramref name="Refusal"/> is set to a non-<see cref="SocialLoginRefusal.None"/> value (refusal),
/// or <paramref name="LinkRequired"/> is non-null (step-up linking required — no session issued).
/// At most one of these discriminators is active at a time.
/// </summary>
/// <param name="User">The resolved or created platform user, or null on refusal/link-required.</param>
/// <param name="IsNew">Whether the user was created as part of this call. Always false on refusal.</param>
/// <param name="Refusal">The refusal reason, or <see cref="SocialLoginRefusal.None"/> on success.</param>
/// <param name="LinkRequired">
/// When non-null, the social callback matched an existing verified account but the link has not been
/// established yet — the caller must start the step-up flow and then call link-confirm.
/// </param>
public record ResolveSocialUserResult(
    PlatformUser? User,
    bool IsNew,
    SocialLoginRefusal Refusal,
    LinkRequiredInfo? LinkRequired = null);

/// <summary>
/// Information surfaced when <see cref="ResolveSocialUserResult.LinkRequired"/> is set.
/// Contains the social identity claims needed to mint the link-pending token.
/// </summary>
/// <param name="Provider">Social provider key, e.g. <c>google</c>.</param>
/// <param name="Subject">Provider's stable subject id.</param>
/// <param name="SocialEmail">Verified email asserted by the provider.</param>
/// <param name="DisplayName">Display name from the social profile (may be null).</param>
/// <param name="TargetAccountId"><see cref="PlatformUser.Id"/> of the existing account the email matched.</param>
public record LinkRequiredInfo(
    string Provider,
    string Subject,
    string SocialEmail,
    string? DisplayName,
    Guid TargetAccountId);

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
    /// Resolves or creates a PlatformUser from social login claims under the
    /// strict link policy introduced in feature 115.
    ///
    /// Resolution order:
    /// <list type="number">
    /// <item>Find by provider+subject (returning user). Refresh
    /// <c>LastUsedAt</c> and <c>DisplayName</c>. <b>No verification re-check</b> —
    /// trust is established at link time (FR-013).</item>
    /// <item>Find by email: when both the provider's <c>EmailVerified</c> claim and the
    /// existing user's <c>EmailVerified</c> are <c>true</c>, return a <see cref="LinkRequiredInfo"/>
    /// result — the caller must complete step-up linking (Feature 168). Otherwise return the
    /// appropriate <see cref="SocialLoginRefusal"/> value.</item>
    /// <item>Create new user only when <paramref name="allowCreate"/> is <c>true</c>
    /// and the provider asserts <c>EmailVerified=true</c> (FR-010). When
    /// <paramref name="allowCreate"/> is <c>false</c> and neither Step 1 nor Step 2
    /// matched, returns <see cref="SocialLoginRefusal.NoExistingAccount"/> and
    /// creates nothing — used on login-only surfaces such as the citizen wallet PWA.
    /// Otherwise return <see cref="SocialLoginRefusal.ProviderUnverified"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="claim">The verified-or-not claim set returned by <see cref="ISocialLoginService.ExchangeCodeAsync"/>. Must have a non-null <c>Subject</c>.</param>
    /// <param name="allowCreate">
    /// When <c>true</c> (default for standard web surfaces), a genuinely-new social identity
    /// will create a new <see cref="PlatformUser"/>. When <c>false</c> (login-only surfaces
    /// such as the citizen wallet PWA), an unknown identity is refused with
    /// <see cref="SocialLoginRefusal.NoExistingAccount"/> and no account is persisted.
    /// Steps 1 and 2 (existing-user lookup and email-collision linking) are unaffected.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ResolveSocialUserResult"/> indicating success, refusal reason, and new-user state.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="claim"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="claim"/>'s <c>Subject</c> is null. Callers should validate the upstream exchange succeeded before invoking this method.</exception>
    Task<ResolveSocialUserResult> ResolveOrCreateSocialUserAsync(
        SocialAuthCallbackResult claim, bool allowCreate, CancellationToken ct);
}
