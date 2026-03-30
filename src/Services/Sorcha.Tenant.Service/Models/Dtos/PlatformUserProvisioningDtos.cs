// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to provision a platform user in a specific organisation.
/// Creates PlatformUser + UserIdentity + PlatformUserOrgMembership atomically.
/// SystemAdmin only.
/// </summary>
public record AdminProvisionUserRequest
{
    /// <summary>
    /// User's email address (unique across platform).
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Target organisation to provision into.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// Role to assign in the organisation (Consumer, Member, Admin).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Optional password (hashed server-side, NIST policy enforced).
    /// If omitted, user must use social login or password reset.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// If true, mark email as verified immediately (no verification email sent).
    /// Default: false.
    /// </summary>
    public bool SkipEmailVerification { get; init; }
}

/// <summary>
/// Response after successful platform user provisioning.
/// </summary>
public record AdminProvisionUserResponse
{
    /// <summary>Created or reused PlatformUser ID.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Created UserIdentity ID.</summary>
    public required Guid UserIdentityId { get; init; }

    /// <summary>User's email.</summary>
    public required string Email { get; init; }

    /// <summary>User's display name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Organisation provisioned into.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>Organisation display name.</summary>
    public required string OrganizationName { get; init; }

    /// <summary>Assigned role.</summary>
    public required string Role { get; init; }

    /// <summary>Whether email is marked verified.</summary>
    public required bool EmailVerified { get; init; }

    /// <summary>Whether an existing PlatformUser was reused (same email, different org).</summary>
    public required bool IsExistingPlatformUser { get; init; }
}

/// <summary>
/// Request to reset a platform user's password. SystemAdmin only.
/// </summary>
public record AdminResetPasswordRequest
{
    /// <summary>
    /// New password (NIST policy enforced).
    /// </summary>
    public required string NewPassword { get; init; }
}
