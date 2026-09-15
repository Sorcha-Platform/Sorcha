// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// DTO for wallet access control entry
/// </summary>
public class WalletAccessDto
{
    /// <summary>
    /// Access entry identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Subject identifier (user or service principal)
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// Access right level (Owner, ReadWrite, ReadOnly)
    /// </summary>
    public required string AccessRight { get; set; }

    /// <summary>
    /// Who granted this access
    /// </summary>
    public required string GrantedBy { get; set; }

    /// <summary>
    /// Reason for granting access
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the access was granted
    /// </summary>
    public DateTime GrantedAt { get; set; }

    /// <summary>
    /// Optional expiration time
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Whether this access is currently active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Sorcha derivation contexts the grant may sign at; null when unscoped (#1643).
    /// </summary>
    public List<string>? AllowedDerivationContexts { get; set; }
}
