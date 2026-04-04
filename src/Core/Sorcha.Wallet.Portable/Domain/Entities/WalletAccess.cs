// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Represents access control and delegation for a wallet
/// </summary>
public class WalletAccess
{
    /// <summary>
    /// Unique identifier for this access entry
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Parent wallet address
    /// </summary>
    public required string ParentWalletAddress { get; set; }

    /// <summary>
    /// Subject identifier (user/service principal) being granted access
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// Type of access granted
    /// </summary>
    public AccessRight AccessRight { get; set; }

    /// <summary>
    /// Reason for granting access (audit trail)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Subject who granted this access
    /// </summary>
    public required string GrantedBy { get; set; }

    /// <summary>
    /// Timestamp when access was granted
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional expiration time for access
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Timestamp when access was revoked
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Subject who revoked this access
    /// </summary>
    public string? RevokedBy { get; set; }

    /// <summary>
    /// Is this access currently active?
    /// </summary>
    public bool IsActive => RevokedAt == null && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);

    /// <summary>
    /// Navigation property to parent wallet
    /// </summary>
    public Wallet? Wallet { get; set; }
}
