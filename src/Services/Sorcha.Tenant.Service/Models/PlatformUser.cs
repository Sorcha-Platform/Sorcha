// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Platform-wide identity anchor. One per person across the entire Sorcha installation.
/// Authentication happens at the platform level via PlatformUser;
/// authorisation is scoped per-organisation via UserIdentity.
/// Stored in the public schema.
/// </summary>
public class PlatformUser
{
    /// <summary>
    /// Unique platform user identifier. Referenced by UserIdentity.PlatformUserId.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Platform-wide unique email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Default display name for this user.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// BCrypt password hash. Null for social-login-only users.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Whether the user's email address has been verified.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Timestamp when the email was verified (UTC).
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Email verification token (URL-safe base64, 32 bytes).
    /// Cleared after successful verification.
    /// </summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Expiry timestamp for the verification token (24 hours from generation).
    /// </summary>
    public DateTimeOffset? VerificationTokenExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 hash of the password reset token.
    /// Storing the hash prevents compromise if the database is leaked.
    /// Cleared after successful reset (one-time use).
    /// </summary>
    public string? PasswordResetTokenHash { get; set; }

    /// <summary>
    /// Expiry timestamp for the password reset token (1 hour from generation).
    /// </summary>
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts (for progressive lockout).
    /// </summary>
    public int FailedLoginCount { get; set; }

    /// <summary>
    /// Timestamp until which the account is temporarily locked. Null if not locked.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Whether the account requires administrator unlock (25+ failed attempts).
    /// </summary>
    public bool LockedPermanently { get; set; }

    /// <summary>
    /// Number of private organisations this user has created.
    /// Enforces MaxOrgsPerUser limit from PlatformSettings.
    /// </summary>
    public int CreatedOrgsCount { get; set; }

    /// <summary>
    /// Platform user account status.
    /// </summary>
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Active;

    /// <summary>
    /// Account registration timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last successful authentication timestamp (UTC).
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Social login provider links (Google, GitHub, Microsoft, Apple).
    /// </summary>
    public ICollection<PlatformSocialLogin> SocialLogins { get; set; } = new List<PlatformSocialLogin>();

    /// <summary>
    /// FIDO2/WebAuthn passkey credentials.
    /// </summary>
    public ICollection<PasskeyCredential> PasskeyCredentials { get; set; } = new List<PasskeyCredential>();

    /// <summary>
    /// Organisation memberships (denormalized lookup for org switcher).
    /// </summary>
    public ICollection<PlatformUserOrgMembership> OrgMemberships { get; set; } = new List<PlatformUserOrgMembership>();
}
