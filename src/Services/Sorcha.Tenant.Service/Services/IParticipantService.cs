// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service interface for participant identity management operations.
/// </summary>
public interface IParticipantService
{

    /// <summary>
    /// Registers a user as a participant in an organization (admin registration).
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="request">Create participant request.</param>
    /// <param name="actorId">ID of the admin performing the registration.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created participant response.</returns>
    Task<ParticipantDetailResponse> RegisterAsync(
        Guid organizationId,
        CreateParticipantRequest request,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-registers the current user as a participant (user self-registration).
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">Current user's ID.</param>
    /// <param name="displayName">Optional display name override.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created participant response.</returns>
    Task<ParticipantDetailResponse> SelfRegisterAsync(
        Guid organizationId,
        Guid userId,
        string? displayName = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);



    /// <summary>
    /// Gets a participant by ID.
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The participant detail response or null if not found.</returns>
    Task<ParticipantDetailResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists participants in an organization with pagination.
    /// </summary>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Page size.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated participant list.</returns>
    Task<ParticipantListResponse> ListAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        ParticipantIdentityStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches participants based on criteria with org-scoped visibility.
    /// </summary>
    /// <param name="request">Search request.</param>
    /// <param name="accessibleOrganizations">Organization IDs the user has access to.</param>
    /// <param name="isSystemAdmin">Whether the user is a system admin.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    Task<ParticipantSearchResponse> SearchAsync(
        ParticipantSearchRequest request,
        IReadOnlyList<Guid>? accessibleOrganizations,
        bool isSystemAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant by wallet address.
    /// </summary>
    /// <param name="walletAddress">Wallet address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The participant response or null if not found.</returns>
    Task<ParticipantResponse?> GetByWalletAddressAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all participant profiles for a user across organizations.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of participant profiles grouped by organization.</returns>
    Task<List<ParticipantDetailResponse>> GetMyProfilesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant by user ID and organization ID (service-to-service lookup).
    /// </summary>
    /// <param name="userId">User ID from Tenant Service.</param>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The participant detail response or null if not found.</returns>
    Task<ParticipantDetailResponse?> GetByUserAndOrgAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);



    /// <summary>
    /// Updates a participant's information.
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="actorId">ID of the user performing the update.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated participant response or null if not found.</returns>
    Task<ParticipantDetailResponse?> UpdateAsync(
        Guid id,
        UpdateParticipantRequest request,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a participant (soft delete).
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="actorId">ID of the user performing the deactivation.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if not found.</returns>
    Task<bool> DeactivateAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a participant (admin action).
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="actorId">ID of the admin performing the suspension.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if not found.</returns>
    Task<bool> SuspendAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a suspended or inactive participant.
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="actorId">ID of the admin performing the reactivation.</param>
    /// <param name="ipAddress">Client IP address for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false if not found.</returns>
    Task<bool> ReactivateAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);



    /// <summary>
    /// Validates that a participant has signing capability (active linked wallet).
    /// </summary>
    /// <param name="id">Participant ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if participant has at least one active linked wallet.</returns>
    Task<bool> ValidateSigningCapabilityAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user is already registered as a participant in the organization.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if already registered.</returns>
    Task<bool> IsRegisteredAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

}
