// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Tracks consumed invitation nonces to prevent replay attacks.
/// Each accepted invitation's nonce is recorded here with a unique index.
/// </summary>
public class InvitationNonce
{
    /// <summary>
    /// Primary key.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The UUID v4 nonce from the invitation payload. Unique-indexed for O(1) replay detection.
    /// </summary>
    public required string Nonce { get; set; }

    /// <summary>
    /// Reference to the invitation that contained this nonce.
    /// </summary>
    public required string InvitationId { get; set; }

    /// <summary>
    /// Organization that issued the invitation.
    /// </summary>
    public Guid SourceOrgId { get; set; }

    /// <summary>
    /// Organization that accepted the invitation.
    /// </summary>
    public Guid TargetOrgId { get; set; }

    /// <summary>
    /// Register the invitation grants access to.
    /// </summary>
    public required string RegisterId { get; set; }

    /// <summary>
    /// When the nonce was consumed (invitation accepted).
    /// </summary>
    public DateTimeOffset ConsumedAt { get; set; } = DateTimeOffset.UtcNow;
}
