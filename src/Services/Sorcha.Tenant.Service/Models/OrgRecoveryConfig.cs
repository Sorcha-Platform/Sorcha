// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Per-organization recovery key configuration (Feature 060).
/// Stores the org's recovery public key for wrapping wallet recovery keys.
/// </summary>
public class OrgRecoveryConfig
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to Organization. One config per org (unique).
    /// </summary>
    public required Guid OrganizationId { get; set; }

    /// <summary>
    /// ED25519 public key for wrapping recovery keys (Base64).
    /// </summary>
    public required string RecoveryPublicKey { get; set; }

    /// <summary>
    /// Stable identifier for this recovery key (used in RecoveryKeyWrap.RecipientKeyId).
    /// </summary>
    public required string RecoveryKeyId { get; set; }

    /// <summary>
    /// Admin who configured recovery.
    /// </summary>
    public required string CreatedBy { get; set; }

    /// <summary>
    /// When configured.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last key rotation timestamp.
    /// </summary>
    public DateTimeOffset? RotatedAt { get; set; }
}

/// <summary>
/// Request to create or update organization recovery configuration.
/// </summary>
public class OrgRecoveryConfigRequest
{
    /// <summary>
    /// ED25519 public key for wrapping recovery keys (Base64).
    /// </summary>
    public required string RecoveryPublicKey { get; set; }
}
