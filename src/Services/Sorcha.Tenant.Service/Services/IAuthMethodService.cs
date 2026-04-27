// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Identifies a sign-in method kind for the last-method floor check
/// in <see cref="IAuthMethodService.WouldRemovingLeaveZeroAsync"/>.
/// TOTP enrolment is intentionally absent — it is a second factor, not a method.
/// </summary>
public enum AuthMethodKind
{
    /// <summary>The account password (presence flag, not a row).</summary>
    Password = 0,

    /// <summary>A linked social provider (<c>PlatformSocialLogin</c> row).</summary>
    Social = 1,

    /// <summary>An active passkey (<c>PasskeyCredential</c> with Status=Active).</summary>
    Passkey = 2
}

/// <summary>
/// Single source of truth for "would removing this method leave the user
/// with zero sign-in methods?". Used by every Remove endpoint to enforce
/// the last-method floor inside the mutation transaction, and by the
/// aggregate-read endpoint to populate the <c>canRemove</c> flag the UI
/// reads. UI and server share the same answer (Feature 116, FR-004 + FR-029).
/// </summary>
public interface IAuthMethodService
{
    /// <summary>
    /// Returns true when removing the identified method would leave the user
    /// with zero <see cref="AuthMethodKind"/> sign-in methods. The
    /// <paramref name="methodId"/> is ignored for <see cref="AuthMethodKind.Password"/>
    /// (there is at most one password) but identifies the specific row for
    /// <see cref="AuthMethodKind.Social"/> and <see cref="AuthMethodKind.Passkey"/>.
    /// </summary>
    Task<bool> WouldRemovingLeaveZeroAsync(
        Guid platformUserId,
        AuthMethodKind kind,
        Guid? methodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the user's currently-active sign-in methods (password presence
    /// + social-link rows + Active passkey rows). Used by the aggregate read
    /// to compute per-row <c>canRemove</c> flags in one query.
    /// </summary>
    Task<AuthMethodCounts> GetCountsAsync(Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Build the aggregate Accounts-tab read for a single user. Returns null
    /// when the user is not found.
    /// </summary>
    Task<Models.Requests.AuthMethodsResponse?> GetAggregateAsync(Guid platformUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshot of the user's current sign-in method counts. Sum of all three
/// is the active-method total against which the floor is enforced.
/// </summary>
/// <param name="HasPassword">True iff <c>PasswordHash</c> is non-null.</param>
/// <param name="SocialCount">Count of <c>PlatformSocialLogin</c> rows.</param>
/// <param name="ActivePasskeyCount">Count of <c>PasskeyCredential</c> with Status=Active.</param>
public readonly record struct AuthMethodCounts(bool HasPassword, int SocialCount, int ActivePasskeyCount)
{
    /// <summary>Sum of password (0/1), social links, and active passkeys.</summary>
    public int Total => (HasPassword ? 1 : 0) + SocialCount + ActivePasskeyCount;
}
