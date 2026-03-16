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
    /// Cross-org identity anchor. Links to PlatformUser in the public schema.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// User email address (denormalized copy from PlatformUser).
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
