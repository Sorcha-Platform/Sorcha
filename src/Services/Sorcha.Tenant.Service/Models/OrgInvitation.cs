// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Time-limited invitation for a user to join an organization with a specified role.
/// Stored in per-organization schema (org_{organization_id}).
/// </summary>
public class OrgInvitation
{
    /// <summary>
    /// Unique invitation identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Organization this invitation belongs to.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Email address the invitation was sent to.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Role assigned to the user upon acceptance.
    /// </summary>
    public UserRole AssignedRole { get; set; } = UserRole.Member;

    /// <summary>
    /// Cryptographic invitation token (32-byte URL-safe base64). Globally unique.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Invitation expiry timestamp. Default: 7 days from creation.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Current invitation status.
    /// </summary>
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    /// <summary>
    /// User who created this invitation.
    /// </summary>
    public Guid InvitedByUserId { get; set; }

    /// <summary>
    /// User who accepted this invitation (may differ from target email if using different account).
    /// </summary>
    public Guid? AcceptedByUserId { get; set; }

    /// <summary>
    /// Timestamp when the invitation was accepted.
    /// </summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// Timestamp when the invitation was revoked by an administrator.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Invitation creation timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
