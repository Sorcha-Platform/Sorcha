// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Authenticated user within an organization.
/// Stored in per-organization schema (org_{organization_id}).
/// </summary>
public class UserIdentity
{
    /// <summary>
    /// Unique user identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Organization membership (denormalized for queries).
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// External IDP subject claim (sub). Unique within organization.
    /// Null for local authentication users.
    /// </summary>
    public string? ExternalIdpSubject { get; set; }

    /// <summary>
    /// Password hash for local authentication (BCrypt).
    /// Null for external IDP users.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// User email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User display name (friendly name shown in UI).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// User roles within organization (Administrator, Auditor, Member, etc.).
    /// Organization creator automatically gets Administrator role.
    /// </summary>
    public UserRole[] Roles { get; set; } = [UserRole.Member];

    /// <summary>
    /// User account status (Active, Suspended, Deleted).
    /// </summary>
    public IdentityStatus Status { get; set; } = IdentityStatus.Active;

    /// <summary>
    /// User creation timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last successful login timestamp (UTC). Null if never logged in.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the user's email address has been verified.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Timestamp when the email was verified.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Email verification token (32-byte URL-safe base64).
    /// Cleared after successful verification.
    /// </summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Expiry timestamp for the verification token (24h from generation).
    /// </summary>
    public DateTimeOffset? VerificationTokenExpiresAt { get; set; }

    /// <summary>
    /// How this user account was provisioned.
    /// </summary>
    public ProvisioningMethod ProvisionedVia { get; set; } = ProvisioningMethod.Local;

    /// <summary>
    /// ID of the user who invited this user (if provisioned via invitation).
    /// </summary>
    public Guid? InvitedByUserId { get; set; }

    /// <summary>
    /// Whether the user has completed their profile (has email and display name).
    /// False if OIDC login didn't return required claims.
    /// </summary>
    public bool ProfileCompleted { get; set; } = true;

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
    /// SHA-256 hash of the password reset token.
    /// Storing the hash prevents token compromise if the database is leaked.
    /// Cleared after successful password reset (one-time use).
    /// </summary>
    public string? PasswordResetTokenHash { get; set; }

    /// <summary>
    /// Expiry timestamp for the password reset token (1 hour from generation).
    /// Null when no reset is pending.
    /// </summary>
    public DateTimeOffset? PasswordResetTokenExpiresAt { get; set; }
}

/// <summary>
/// User roles within an organization.
/// Consolidated from 8 to 5 roles — Developer, User, Consumer mapped to Member.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserRole
{
    /// <summary>
    /// System administrator with elevated privileges across all organizations.
    /// </summary>
    SystemAdmin = 0,

    /// <summary>
    /// Full administrative access to organization settings, users, and permissions.
    /// </summary>
    Administrator = 1,

    /// <summary>
    /// Blueprint designer who can create and manage workflow definitions.
    /// </summary>
    Designer = 2,

    /// <summary>
    /// Read-only access to audit logs and organization activity.
    /// </summary>
    Auditor = 3,

    /// <summary>
    /// Standard member with permissions defined by organization policy.
    /// </summary>
    Member = 4
}

/// <summary>
/// User account status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentityStatus
{
    /// <summary>
    /// User account is active and can authenticate.
    /// </summary>
    Active,

    /// <summary>
    /// User account is temporarily suspended (cannot authenticate).
    /// </summary>
    Suspended,

    /// <summary>
    /// User account is soft-deleted (can be restored within 30 days).
    /// </summary>
    Deleted
}
