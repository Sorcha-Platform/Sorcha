// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service interface for organization management operations.
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// Creates a new organization.
    /// </summary>
    /// <param name="request">Create organization request.</param>
    /// <param name="creatorUserId">ID of the user creating the organization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created organization response.</returns>
    Task<OrganizationResponse> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        Guid creatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by ID.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organization response or null if not found.</returns>
    Task<OrganizationResponse?> GetOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an organization by subdomain.
    /// </summary>
    /// <param name="subdomain">Organization subdomain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The organization response or null if not found.</returns>
    Task<OrganizationResponse?> GetOrganizationBySubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all organizations (admin only).
    /// </summary>
    /// <param name="includeInactive">Whether to include suspended/deleted organizations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of organizations.</returns>
    Task<OrganizationListResponse> ListOrganizationsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an organization.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated organization response or null if not found.</returns>
    Task<OrganizationResponse?> UpdateOrganizationAsync(
        Guid id,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates (soft deletes) an organization.
    /// </summary>
    /// <param name="id">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if not found.</returns>
    Task<bool> DeactivateOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Add user request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created user response.</returns>
    Task<UserResponse> AddUserToOrganizationAsync(
        Guid organizationId,
        AddUserToOrganizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users in an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="includeInactive">Whether to include suspended/deleted users.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of users.</returns>
    Task<UserListResponse> GetOrganizationUsersAsync(
        Guid organizationId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific user in an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user response or null if not found.</returns>
    Task<UserResponse?> GetOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a user in an organization.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated user response or null if not found.</returns>
    Task<UserResponse?> UpdateOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user from an organization (soft delete).
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if not found.</returns>
    Task<bool> RemoveUserFromOrganizationAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a subdomain format and availability.
    /// </summary>
    /// <param name="subdomain">Subdomain to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with message.</returns>
    Task<(bool IsValid, string? ErrorMessage)> ValidateSubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets organization statistics (count of organizations and users).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Organization statistics.</returns>
    Task<OrganizationStatsResponse> GetOrganizationStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the allowed email domains for auto-provisioning.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Allowed domains and whether restrictions are active, or null if org not found.</returns>
    Task<DomainRestrictionsResponse?> GetDomainRestrictionsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the allowed email domains for auto-provisioning.
    /// Empty array disables restrictions.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="allowedDomains">List of allowed email domains.</param>
    /// <param name="updatedByUserId">ID of the admin making the change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated restrictions, or null if org not found.</returns>
    Task<DomainRestrictionsResponse?> UpdateDomainRestrictionsAsync(
        Guid organizationId,
        string[] allowedDomains,
        Guid updatedByUserId,
        CancellationToken cancellationToken = default);
}
