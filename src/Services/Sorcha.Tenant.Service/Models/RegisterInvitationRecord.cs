// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Persistent record of a register invitation, tracking its lifecycle
/// from creation through acceptance or revocation.
/// </summary>
public class RegisterInvitationRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Unique invitation identifier (UUID, referenced in token payload).</summary>
    public required string InvitationId { get; set; }

    /// <summary>Organization that created the invitation.</summary>
    public Guid SourceOrgId { get; set; }

    /// <summary>Target organization DID (did:sorcha:org:{address}).</summary>
    public required string TargetOrgDid { get; set; }

    /// <summary>Target org's wallet address (resolved at creation time).</summary>
    public required string TargetWalletAddress { get; set; }

    /// <summary>Register the invitation grants access to.</summary>
    public required string RegisterId { get; set; }

    /// <summary>Cached register name at creation time.</summary>
    public string? RegisterName { get; set; }

    /// <summary>Nonce embedded in the token payload (for cross-referencing with InvitationNonce).</summary>
    public required string Nonce { get; set; }

    /// <summary>Invitation lifecycle status.</summary>
    public RegisterInvitationStatus Status { get; set; } = RegisterInvitationStatus.Pending;

    /// <summary>When the invitation expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the invitation was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Admin user who created the invitation.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When the invitation was accepted (null if not accepted).</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>When the invitation was revoked (null if not revoked).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>FK navigation property.</summary>
    public Organization? SourceOrganization { get; set; }
}

/// <summary>
/// Invitation lifecycle status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegisterInvitationStatus
{
    /// <summary>Invitation created, awaiting acceptance.</summary>
    Pending,

    /// <summary>Invitation accepted by target organization.</summary>
    Accepted,

    /// <summary>Invitation revoked by source organization.</summary>
    Revoked,

    /// <summary>Invitation expired without being accepted.</summary>
    Expired
}
