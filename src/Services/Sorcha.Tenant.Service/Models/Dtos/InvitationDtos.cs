// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to create an organization invitation.
/// </summary>
public record CreateInvitationRequest
{
    /// <summary>
    /// Email address to send the invitation to.
    /// </summary>
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Role to assign when the invitation is accepted (Administrator, Designer, Auditor, Member).
    /// </summary>
    [Required]
    public UserRole Role { get; init; } = UserRole.Member;

    /// <summary>
    /// Number of days until the invitation expires (1-30, default 7).
    /// </summary>
    [Range(1, 30)]
    public int ExpiryDays { get; init; } = 7;
}

/// <summary>
/// Invitation response returned to clients.
/// </summary>
public record InvitationResponse
{
    /// <summary>
    /// Unique invitation identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Email address the invitation was sent to.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Role assigned upon acceptance.
    /// </summary>
    public string AssignedRole { get; init; } = string.Empty;

    /// <summary>
    /// Current invitation status (Pending, Accepted, Expired, Revoked).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// When the invitation expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Display name of the user who sent the invitation.
    /// </summary>
    public string InvitedBy { get; init; } = string.Empty;

    /// <summary>
    /// When the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
