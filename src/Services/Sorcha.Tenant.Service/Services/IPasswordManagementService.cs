// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Self-service password lifecycle for a signed-in platform user (Feature 116 US3).
/// Each method enforces business rules (already-set, no-current-password, last-method
/// floor, password policy) and is the single source of truth for whether a particular
/// transition is allowed; HTTP endpoints map outcomes to status codes.
/// </summary>
public interface IPasswordManagementService
{
    /// <summary>
    /// Sets a password on a platform user that does not currently have one. Returns
    /// <see cref="PasswordSetOutcome.AlreadySet"/> if the user already has a password
    /// (caller must use <see cref="ChangeAsync"/>). The supplied password is run
    /// through <see cref="IPasswordPolicyService"/> before hashing.
    /// </summary>
    Task<PasswordSetOutcome> SetAsync(
        Guid platformUserId,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the password on a platform user that already has one. Returns
    /// <see cref="PasswordChangeOutcome.NoCurrentPassword"/> if no password is set
    /// (caller must use <see cref="SetAsync"/>). The current password is not
    /// re-checked here — possession was proven by the re-authentication challenge
    /// the endpoint enforced before invoking this method.
    /// </summary>
    Task<PasswordChangeOutcome> ChangeAsync(
        Guid platformUserId,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the password from a platform user. Returns
    /// <see cref="PasswordRemoveOutcome.BlockedByFloor"/> if removing the password
    /// would leave the user with zero remaining sign-in methods (the floor is
    /// re-checked inside the same <c>SaveChanges</c> as the mutation).
    /// </summary>
    Task<PasswordRemoveOutcome> RemoveAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True iff the platform user has zero sign-in methods at all (no password,
    /// no socials, no active passkeys). Endpoints use this to decide whether the
    /// re-authentication challenge requirement on <c>POST /password/set</c> can
    /// be bypassed (the bootstrap mode — see design Q5/§4.4).
    /// </summary>
    Task<bool> IsBootstrapModeAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default);
}
