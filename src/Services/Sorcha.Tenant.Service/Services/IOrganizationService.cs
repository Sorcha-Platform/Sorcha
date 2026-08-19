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
    /// Records a wallet the ORG ADMIN created as this organisation's canonical signing wallet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The link half of create-then-link (#1525). The admin creates the wallet against the Wallet
    /// Service directly, so the BIP39 recovery phrase goes straight from there to them and never
    /// transits this service — it is shown once and never stored, and it is the organisation's
    /// secret rather than the platform's.
    /// </para>
    /// <para>
    /// The wallet's owner must be this organisation, or an admin could adopt a wallet they merely
    /// know the address of. Returns <c>null</c> when the org does not exist.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The organisation already has a wallet, or the address does not resolve to a wallet owned by it.
    /// </exception>
    Task<OrganizationResponse?> LinkOrganizationWalletAsync(
        Guid organizationId,
        string walletAddress,
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
    /// Provisions a NEW org-scoped password user directly into an organisation (single-org — no
    /// public account, no invitation). The verified-email bypass is gated by
    /// <c>Platform:AllowAdminVerifiedUserCreation</c>. Spec 136 follow-up.
    /// </summary>
    /// <param name="organizationId">Target organisation.</param>
    /// <param name="request">Provision request (email, display name, password, roles, emailVerified).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created user response.</returns>
    Task<UserResponse> ProvisionOrgUserAsync(
        Guid organizationId,
        ProvisionOrgUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users in an organization with optional filtering by verification and invitation status.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="includeInactive">Whether to include suspended/deleted users.</param>
    /// <param name="emailVerified">Filter by email verification status (null = no filter).</param>
    /// <param name="provisionedVia">Filter by provisioning method (null = no filter).</param>
    /// <param name="includePending">Include pending OrgInvitation records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of users with optional pending invitations.</returns>
    Task<UserListResponse> GetOrganizationUsersAsync(
        Guid organizationId,
        bool includeInactive = false,
        bool? emailVerified = null,
        string? provisionedVia = null,
        bool includePending = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Administratively marks a user's email as verified without requiring the email verification loop.
    /// </summary>
    /// <param name="organizationId">Organization the user belongs to.</param>
    /// <param name="userId">User ID to verify.</param>
    /// <param name="adminUserId">ID of the admin performing the override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if verified, false if already verified. Throws if user not found.</returns>
    Task<bool> AdminVerifyEmailAsync(
        Guid organizationId,
        Guid userId,
        Guid adminUserId,
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
