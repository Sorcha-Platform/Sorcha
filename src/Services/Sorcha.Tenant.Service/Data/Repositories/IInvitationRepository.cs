// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Repository interface for OrgInvitation operations.
/// </summary>
public interface IInvitationRepository
{
    /// <summary>
    /// Gets an invitation by ID.
    /// </summary>
    Task<OrgInvitation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an invitation by its unique token.
    /// </summary>
    Task<OrgInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets invitations for an organization, optionally filtered by status.
    /// </summary>
    Task<List<OrgInvitation>> GetByOrganizationAsync(Guid organizationId, InvitationStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an active (Pending) invitation exists for the email in the organization.
    /// </summary>
    Task<bool> HasActiveInvitationAsync(Guid organizationId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new invitation.
    /// </summary>
    Task<OrgInvitation> CreateAsync(OrgInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing invitation.
    /// </summary>
    Task<OrgInvitation> UpdateAsync(OrgInvitation invitation, CancellationToken cancellationToken = default);
}
