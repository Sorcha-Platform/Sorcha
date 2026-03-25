// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to add a user to an organization.
/// </summary>
public record AddUserToOrganizationRequest
{
    /// <summary>
    /// User email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// External IDP subject claim (from OIDC token).
    /// </summary>
    public required string ExternalIdpSubject { get; init; }

    /// <summary>
    /// Roles to assign to the user.
    /// </summary>
    public UserRole[] Roles { get; init; } = [UserRole.Member];
}

/// <summary>
/// Request to update a user's details.
/// </summary>
public record UpdateUserRequest
{
    /// <summary>
    /// Updated display name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Updated roles.
    /// </summary>
    public UserRole[]? Roles { get; init; }

    /// <summary>
    /// Updated status.
    /// </summary>
    public IdentityStatus? Status { get; init; }
}

/// <summary>
/// User response DTO with enhanced verification and invitation status.
/// </summary>
public record UserResponse
{
    /// <summary>
    /// User ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Organization ID.
    /// </summary>
    public Guid OrganizationId { get; init; }

    /// <summary>
    /// User email address.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// User display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// User roles.
    /// </summary>
    public UserRole[] Roles { get; init; } = [];

    /// <summary>
    /// User account lifecycle status.
    /// </summary>
    public IdentityStatus Status { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last login timestamp.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; init; }

    /// <summary>
    /// Whether the user has verified their email address (sourced from PlatformUser).
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>
    /// Timestamp when email was verified (sourced from PlatformUser).
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; init; }

    /// <summary>
    /// How the user was provisioned (Local, Oidc, Invitation, SocialLogin, AdminCreated, Passkey).
    /// </summary>
    public string ProvisionedVia { get; init; } = string.Empty;

    /// <summary>
    /// ID of the admin who invited this user, if applicable.
    /// </summary>
    public Guid? InvitedByUserId { get; init; }

    /// <summary>
    /// Whether the user has completed their profile.
    /// </summary>
    public bool ProfileCompleted { get; init; }

    /// <summary>
    /// Status of the organisation invitation for this user, if any (Pending, Accepted, Expired, Revoked).
    /// </summary>
    public string? InvitationStatus { get; init; }

    /// <summary>
    /// Creates a response from a UserIdentity entity with optional PlatformUser and OrgInvitation data.
    /// </summary>
    public static UserResponse FromEntity(UserIdentity user, PlatformUser? platformUser = null, OrgInvitation? invitation = null) => new()
    {
        Id = user.Id,
        OrganizationId = user.OrganizationId,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Roles = user.Roles,
        Status = user.Status,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        EmailVerified = platformUser?.EmailVerified ?? false,
        EmailVerifiedAt = platformUser?.EmailVerifiedAt,
        ProvisionedVia = user.ProvisionedVia.ToString(),
        InvitedByUserId = user.InvitedByUserId,
        ProfileCompleted = user.ProfileCompleted,
        InvitationStatus = invitation?.Status.ToString()
    };
}

/// <summary>
/// Request to change a user's role.
/// </summary>
public record ChangeUserRoleRequest
{
    /// <summary>
    /// New role to assign (Administrator, Designer, Auditor, Member). SystemAdmin not allowed.
    /// </summary>
    public required UserRole Role { get; init; }
}

/// <summary>
/// Response for a pending organisation invitation (user has not yet accepted — no UserIdentity exists).
/// </summary>
public record PendingInvitationResponse
{
    /// <summary>
    /// Email address the invitation was sent to.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Role that will be assigned upon acceptance.
    /// </summary>
    public UserRole AssignedRole { get; init; }

    /// <summary>
    /// Current invitation status (Pending or Expired).
    /// </summary>
    public string InvitationStatus { get; init; } = string.Empty;

    /// <summary>
    /// ID of the admin who sent the invitation.
    /// </summary>
    public Guid InvitedByUserId { get; init; }

    /// <summary>
    /// Invitation expiry timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// When the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Creates a response from an OrgInvitation entity.
    /// </summary>
    public static PendingInvitationResponse FromEntity(OrgInvitation invitation) => new()
    {
        Email = invitation.Email,
        AssignedRole = invitation.AssignedRole,
        InvitationStatus = invitation.Status.ToString(),
        InvitedByUserId = invitation.InvitedByUserId,
        ExpiresAt = invitation.ExpiresAt,
        CreatedAt = invitation.CreatedAt
    };
}

/// <summary>
/// User list response with pagination and optional pending invitations.
/// </summary>
public record UserListResponse
{
    /// <summary>
    /// List of users in the organisation.
    /// </summary>
    public IReadOnlyList<UserResponse> Users { get; init; } = [];

    /// <summary>
    /// Total count of matching users.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Pending invitations (only populated when includePendingInvitations=true).
    /// </summary>
    public IReadOnlyList<PendingInvitationResponse> PendingInvitations { get; init; } = [];

    /// <summary>
    /// Count of pending invitations for this organisation.
    /// </summary>
    public int PendingInvitationCount { get; init; }
}
