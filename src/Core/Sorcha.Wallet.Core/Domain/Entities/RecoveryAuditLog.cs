// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Immutable audit trail for wallet recovery operations.
/// </summary>
public class RecoveryAuditLog
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User whose wallets were recovered.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Organization context.
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>
    /// Which recovery path was used.
    /// </summary>
    public RecoveryPathType RecoveryPath { get; set; }

    /// <summary>
    /// User ID of the initiator (self for passkey, org admin for org-managed).
    /// </summary>
    public required string InitiatedBy { get; set; }

    /// <summary>
    /// Number of wallets restored.
    /// </summary>
    public int WalletsRecovered { get; set; }

    /// <summary>
    /// Number of delegations revoked during recovery.
    /// </summary>
    public int DelegationsRevoked { get; set; }

    /// <summary>
    /// Number of delegations the user chose to preserve.
    /// </summary>
    public int DelegationsPreserved { get; set; }

    /// <summary>
    /// Client IP address for audit.
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// When recovery completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
