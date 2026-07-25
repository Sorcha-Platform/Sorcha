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
    Task<OrgInvitationResponse> CreateInvitationAsync(
        Guid organizationId,
        CreateOrgInvitationRequest request,
        Guid invitedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists invitations for an organization, optionally filtered by status.
    /// </summary>
    Task<List<OrgInvitationResponse>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a pending invitation.
    /// </summary>
    /// <summary>
    /// Re-sends a pending invitation's email, rotating its token and resetting its expiry.
    /// </summary>
    /// <remarks>
    /// Issue #1256. The admin UI used to show a Resend button whose client method POSTed to a route
    /// the service never implemented: the call 404'd, the page discarded the returned <c>false</c>,
    /// and the operator saw "Invitation resent" every time. DRIFT-004 removed the button rather than
    /// faking success, and <c>OrgInvitationWireContractTests</c> now fails the build if a client
    /// operation has no endpoint behind it — so the endpoint had to land first.
    /// <para>
    /// <b>Resend ROTATES the token</b> and resets <c>ExpiresAt</c>. An operator resending usually does
    /// so because the original never arrived or has gone stale, and rotating bounds how long any
    /// leaked token stays live. The cost is that a link already in flight stops working — accepted,
    /// because the newly-sent email supersedes it and is the one the invitee will use.
    /// </para>
    /// </remarks>
    /// <returns><see langword="false"/> when no such invitation exists for the organisation.</returns>
    /// <exception cref="InvalidOperationException">The invitation is not Pending.</exception>
    Task<bool> ResendInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid resentByUserId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid revokedByUserId,
        CancellationToken cancellationToken = default);
}
