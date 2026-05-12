// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Data transfer object for wallet information
/// </summary>
public class WalletDto
{
    /// <summary>
    /// Wallet address (public identifier)
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// Friendly name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Public key (hex encoded)
    /// </summary>
    public required string PublicKey { get; set; }

    /// <summary>
    /// Cryptographic algorithm
    /// </summary>
    public required string Algorithm { get; set; }

    /// <summary>
    /// Wallet status (Active, Archived, Deleted, Locked)
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Owner identifier
    /// </summary>
    public required string Owner { get; set; }

    /// <summary>
    /// Tenant identifier
    /// </summary>
    public required string Tenant { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Optional metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
