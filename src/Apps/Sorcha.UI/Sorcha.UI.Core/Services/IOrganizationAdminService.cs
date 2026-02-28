// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side service for organization and user administration operations.
/// Wraps HTTP calls to the Tenant Service API.
/// </summary>
public interface IOrganizationAdminService
{
    /// <summary>
    /// Lists all organizations.
    /// </summary>
    /// <param name="includeInactive">Include inactive organizations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of organizations.</returns>
    Task<OrganizationListResult> ListOrganizationsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific organization by ID.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Organization details, or null if not found.</returns>
    Task<OrganizationDto?> GetOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new organization.
    /// </summary>
    /// <param name="request">Create request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created organization.</returns>
    Task<OrganizationDto> CreateOrganizationAsync(
        CreateOrganizationDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing organization.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated organization.</returns>
    Task<OrganizationDto?> UpdateOrganizationAsync(
        Guid id,
        UpdateOrganizationDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates (soft-deletes) an organization.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DeactivateOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates subdomain availability.
    /// </summary>
    /// <param name="subdomain">Subdomain to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<SubdomainValidationResult> ValidateSubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets platform statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Platform statistics.</returns>
    Task<PlatformKpis> GetPlatformStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists users in an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="includeInactive">Include inactive users.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of users.</returns>
    Task<UserListResult> GetOrganizationUsersAsync(
        Guid organizationId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific user.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User details, or null if not found.</returns>
    Task<UserDto?> GetOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Add user request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created user.</returns>
    Task<UserDto> AddUserToOrganizationAsync(
        Guid organizationId,
        AddUserDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user in an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated user.</returns>
    Task<UserDto?> UpdateOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        UpdateUserDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user from an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> RemoveUserFromOrganizationAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

}

/// <summary>
/// Organization DTO for client-side use.
/// </summary>
public record OrganizationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedAt { get; init; }
    public BrandingDto? Branding { get; init; }
}

/// <summary>
/// Branding configuration DTO.
/// </summary>
public record BrandingDto
{
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? CompanyTagline { get; init; }
}

/// <summary>
/// Request to create an organization.
/// </summary>
public record CreateOrganizationDto
{
    public required string Name { get; init; }
    public required string Subdomain { get; init; }
    public BrandingDto? Branding { get; init; }
}

/// <summary>
/// Request to update an organization.
/// </summary>
public record UpdateOrganizationDto
{
    public string? Name { get; init; }
    public string? Status { get; init; }
    public BrandingDto? Branding { get; init; }
}

/// <summary>
/// Organization list result.
/// </summary>
public record OrganizationListResult
{
    public IReadOnlyList<OrganizationDto> Organizations { get; init; } = [];
    public int TotalCount { get; init; }
}

/// <summary>
/// Subdomain validation result.
/// </summary>
public record SubdomainValidationResult
{
    public string Subdomain { get; init; } = string.Empty;
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// User DTO for client-side use.
/// </summary>
public record UserDto
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string[] Roles { get; init; } = [];
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>
/// Request to add a user.
/// </summary>
public record AddUserDto
{
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string ExternalIdpUserId { get; init; }
    public string[] Roles { get; init; } = ["Member"];
}

/// <summary>
/// Request to update a user.
/// </summary>
public record UpdateUserDto
{
    public string? DisplayName { get; init; }
    public string[]? Roles { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// User list result.
/// </summary>
public record UserListResult
{
    public IReadOnlyList<UserDto> Users { get; init; } = [];
    public int TotalCount { get; init; }
}
