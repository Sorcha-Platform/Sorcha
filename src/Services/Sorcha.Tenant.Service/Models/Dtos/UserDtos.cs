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
/// User response DTO.
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
    /// User status.
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
    /// Creates a response from a UserIdentity entity.
    /// </summary>
    public static UserResponse FromEntity(UserIdentity user) => new()
    {
        Id = user.Id,
        OrganizationId = user.OrganizationId,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Roles = user.Roles,
        Status = user.Status,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt
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
/// User list response with pagination.
/// </summary>
public record UserListResponse
{
    /// <summary>
    /// List of users.
    /// </summary>
    public IReadOnlyList<UserResponse> Users { get; init; } = [];

    /// <summary>
    /// Total count of users.
    /// </summary>
    public int TotalCount { get; init; }
}
