// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for managing organization invitations.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Creates and sends an invitation to join an organization.
    /// </summary>
    Task<InvitationResponse> CreateInvitationAsync(
        Guid organizationId,
        CreateInvitationRequest request,
        Guid invitedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists invitations for an organization, optionally filtered by status.
    /// </summary>
    Task<List<InvitationResponse>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a pending invitation.
    /// </summary>
    Task<bool> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid revokedByUserId,
        CancellationToken cancellationToken = default);
}
