// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for managing private register invitations.
/// Handles invitation creation (sign + encrypt), acceptance (decrypt + verify),
/// nonce replay protection, and invitation lifecycle management.
/// </summary>
public interface IRegisterInvitationService
{
    /// <summary>
    /// Creates a signed and encrypted invitation token for a target organization
    /// to join a private register owned by the source organization.
    /// </summary>
    /// <param name="sourceOrgId">Organization creating the invitation (must own the register).</param>
    /// <param name="request">Invitation creation request with register ID and target org DID.</param>
    /// <param name="createdByUserId">Admin user creating the invitation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created invitation response with the token.</returns>
    Task<InvitationCreatedResponse> CreateAsync(
        Guid sourceOrgId,
        CreateRegisterInvitationRequest request,
        Guid createdByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Accepts an invitation token: decrypts, verifies signature, checks nonce,
    /// validates expiry and target org, then creates an Invited subscription.
    /// </summary>
    /// <param name="targetOrgId">Organization accepting the invitation.</param>
    /// <param name="request">Request containing the invitation token.</param>
    /// <param name="acceptedByUserId">Admin user accepting the invitation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Accepted invitation response with the new subscription.</returns>
    Task<InvitationAcceptedResponse> AcceptAsync(
        Guid targetOrgId,
        AcceptInvitationRequest request,
        Guid acceptedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists invitations for an organization, optionally filtered by direction.
    /// </summary>
    /// <param name="orgId">Organization to list invitations for.</param>
    /// <param name="direction">Filter: "sent", "received", or "all".</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of invitation summaries.</returns>
    Task<InvitationListResponse> ListAsync(
        Guid orgId,
        string direction = "all",
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a pending invitation (source org only).
    /// </summary>
    /// <param name="orgId">Organization that created the invitation.</param>
    /// <param name="invitationId">Invitation to revoke.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RevokeAsync(Guid orgId, string invitationId, CancellationToken ct = default);
}
