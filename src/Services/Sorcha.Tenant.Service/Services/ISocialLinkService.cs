// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Outcome of a <see cref="ISocialLinkService.LinkAsync"/> call.
/// </summary>
public enum SocialLinkOutcome
{
    /// <summary>Link inserted successfully.</summary>
    Linked = 0,

    /// <summary>
    /// The provider's <c>(Provider, Subject)</c> pair is already linked to a
    /// different <see cref="Models.PlatformUser"/>.
    /// </summary>
    AlreadyLinkedToDifferentUser = 1,

    /// <summary>
    /// The provider returned an email address that already belongs to a
    /// different PlatformUser. Feature 116 / Q1 — strict reject, no merge.
    /// </summary>
    EmailCollision = 2,

    /// <summary>
    /// Caller already has this provider linked to their own account. Idempotent
    /// — no insert needed.
    /// </summary>
    AlreadyLinkedToCaller = 3,
}

/// <summary>
/// Result of an unlink call.
/// </summary>
public enum SocialUnlinkOutcome
{
    /// <summary>Row was hard-deleted.</summary>
    Unlinked = 0,

    /// <summary>Link not found, or not owned by the caller.</summary>
    NotFound = 1,

    /// <summary>Removing this link would leave the user with zero sign-in methods.</summary>
    FloorViolation = 2,
}

/// <summary>
/// Manages post-login social-provider linking and unlinking against the
/// signed-in <see cref="Models.PlatformUser"/>. Enforces the locked
/// decisions from Feature 116 design Q1, Q4, Q6:
///   - Reject on email collision (no automatic merge).
///   - Hard-delete on unlink (provider's own log is the audit trail).
///   - Last-method floor enforced server-side via <see cref="IAuthMethodService"/>.
/// </summary>
public interface ISocialLinkService
{
    /// <summary>
    /// Add a social-provider link to <paramref name="platformUserId"/>. Runs
    /// the email-collision check from Q1; returns
    /// <see cref="SocialLinkOutcome.AlreadyLinkedToDifferentUser"/> if the
    /// <c>(provider, providerSubject)</c> pair belongs to another user.
    /// Idempotent for the caller's own existing link.
    /// </summary>
    Task<SocialLinkOutcome> LinkAsync(
        Guid platformUserId,
        string provider,
        string providerSubject,
        string? providerEmail,
        string? providerDisplayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-delete a social-provider link iff it belongs to
    /// <paramref name="platformUserId"/> and removing it would leave at least
    /// one sign-in method. Floor check runs inside the mutation transaction.
    /// </summary>
    Task<SocialUnlinkOutcome> UnlinkAsync(
        Guid platformUserId,
        Guid linkId,
        CancellationToken cancellationToken = default);
}
