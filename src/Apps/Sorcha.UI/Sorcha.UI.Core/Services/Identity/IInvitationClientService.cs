// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Identity;

/// <summary>
/// Client service for managing organization invitations via the Tenant Service API.
/// </summary>
public interface IInvitationClientService
{
    /// <summary>
    /// Gets a list of invitations for the specified organization, optionally filtered by status.
    /// </summary>
    Task<InvitationListResult> GetInvitationsAsync(Guid organizationId, string? status = null, CancellationToken ct = default);

    /// <summary>
    /// Gets a specific invitation by ID.
    /// </summary>
    Task<InvitationDto?> GetInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new invitation for the specified organization.
    /// </summary>
    Task<InvitationDto> CreateInvitationAsync(Guid organizationId, CreateInvitationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Revokes a pending invitation.
    /// </summary>
    Task<bool> RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken ct = default);

    /// <summary>
    /// Resends a pending invitation email.
    /// </summary>
    Task<bool> ResendInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken ct = default);
}

/// <summary>
/// Represents an organization invitation.
/// </summary>
public record InvitationDto
{
    /// <summary>Invitation identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Email address the invitation was sent to.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Role assigned upon acceptance.</summary>
    public string AssignedRole { get; init; } = "Member";

    /// <summary>Current invitation status (Pending, Accepted, Expired, Revoked).</summary>
    public string Status { get; init; } = "Pending";

    /// <summary>When the invitation expires.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>When the invitation was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>User who sent the invitation.</summary>
    public Guid InvitedByUserId { get; init; }

    /// <summary>User who accepted the invitation.</summary>
    public Guid? AcceptedByUserId { get; init; }

    /// <summary>When the invitation was accepted.</summary>
    public DateTimeOffset? AcceptedAt { get; init; }
}

/// <summary>
/// Request to create a new organization invitation.
/// </summary>
public record CreateInvitationRequest
{
    /// <summary>Email address to send the invitation to.</summary>
    public required string Email { get; init; }

    /// <summary>Role to assign when the invitation is accepted.</summary>
    public string Role { get; init; } = "Member";

    /// <summary>Number of days until the invitation expires (1-30).</summary>
    public int ExpiryDays { get; init; } = 7;
}

/// <summary>
/// Result containing a list of invitations with total count.
/// </summary>
public record InvitationListResult
{
    /// <summary>List of invitations.</summary>
    public IReadOnlyList<InvitationDto> Invitations { get; init; } = [];

    /// <summary>Total number of matching invitations.</summary>
    public int TotalCount { get; init; }
}
