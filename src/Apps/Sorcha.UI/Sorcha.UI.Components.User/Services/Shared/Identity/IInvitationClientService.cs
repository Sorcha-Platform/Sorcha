// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Client service for managing <b>organisation user invitations</b> via the Tenant Service API —
/// emailing a person a token that grants them a role in an organisation.
/// </summary>
/// <remarks>
/// Not to be confused with <b>register invitations</b>
/// (<c>Sorcha.ServiceClients.Invitation.IRegisterInvitationServiceClient</c>), the
/// cryptographic org-to-org envelope that brings another organisation onto a private register.
/// The two concepts share the word "invitation" and nothing else; their request shapes are
/// disjoint. Both request types were once literally called <c>CreateInvitationRequest</c> in
/// different projects — a merge trap this naming exists to close.
/// <para>
/// The surface mirrors the Tenant Service's <c>/api/organizations/{id}/invitations</c> endpoints
/// exactly: create, list, revoke. It deliberately declares nothing else — see
/// <c>OrgInvitationWireContractTests</c>.
/// </para>
/// </remarks>
public interface IInvitationClientService
{
    /// <summary>
    /// Lists invitations for the specified organisation, optionally filtered by status
    /// (Pending, Accepted, Expired, Revoked).
    /// </summary>
    /// <remarks>
    /// The server returns a bare JSON array, so this returns a plain list. It previously returned a
    /// <c>{ invitations, totalCount }</c> envelope the server has never sent, which made every call
    /// throw <see cref="System.Text.Json.JsonException"/>.
    /// </remarks>
    Task<IReadOnlyList<OrgInvitationDto>> GetInvitationsAsync(Guid organizationId, string? status = null, CancellationToken ct = default);

    /// <summary>
    /// Creates and sends a new invitation for the specified organisation.
    /// </summary>
    Task<OrgInvitationDto> CreateInvitationAsync(Guid organizationId, CreateOrgInvitationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Revokes a pending invitation. Returns false if the server rejected the request.
    /// </summary>
    Task<bool> RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken ct = default);
}

/// <summary>
/// An organisation user invitation as returned by the Tenant Service.
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.OrgInvitationResponse</c>.
/// </summary>
public record OrgInvitationDto
{
    /// <summary>Invitation identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Email address the invitation was sent to.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Role assigned upon acceptance.</summary>
    public string AssignedRole { get; init; } = "Consumer";

    /// <summary>Current invitation status (Pending, Accepted, Expired, Revoked).</summary>
    public string Status { get; init; } = "Pending";

    /// <summary>When the invitation expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Display name of the user who sent the invitation.</summary>
    /// <remarks>
    /// A display name, not an identifier. This was previously declared as
    /// <c>InvitedByUserId</c> (<see cref="Guid"/>), which no server response has ever carried.
    /// </remarks>
    public string InvitedBy { get; init; } = string.Empty;

    /// <summary>When the invitation was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Request to create a new organisation user invitation.
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.CreateOrgInvitationRequest</c>.
/// </summary>
public record CreateOrgInvitationRequest
{
    /// <summary>Email address to send the invitation to.</summary>
    public required string Email { get; init; }

    /// <summary>Role to assign when the invitation is accepted.</summary>
    /// <remarks>
    /// Serialised as a string against a server-side <c>UserRole</c> enum. The Tenant Service
    /// applies kebab-case string-enum serialisation, so the accepted spellings are pinned by
    /// <c>OrgInvitationWireContractTests</c> rather than left to inference.
    /// </remarks>
    public string Role { get; init; } = "Consumer";

    /// <summary>Number of days until the invitation expires (1-30).</summary>
    public int ExpiryDays { get; init; } = 7;
}
