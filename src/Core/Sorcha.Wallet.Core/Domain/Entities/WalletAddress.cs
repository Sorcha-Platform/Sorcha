// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Represents a derived address from an HD wallet
/// </summary>
public class WalletAddress
{
    /// <summary>
    /// Unique identifier for this address
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Parent wallet address
    /// </summary>
    public required string ParentWalletAddress { get; set; }

    /// <summary>
    /// The derived public address
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// BIP44 derivation path (e.g., m/44'/0'/0'/0/0)
    /// </summary>
    public required string DerivationPath { get; set; }

    /// <summary>
    /// Index in the derivation sequence
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Is this a change address? (BIP44 change parameter)
    /// </summary>
    public bool IsChange { get; set; }

    /// <summary>
    /// Optional label for this address
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Whether this address has been used in a transaction
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// Timestamp when address was generated
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when address was first used
    /// </summary>
    public DateTime? FirstUsedAt { get; set; }

    /// <summary>
    /// Timestamp when address was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Public key for this derived address (base64 encoded)
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>
    /// Optional notes about this address
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Tags for categorization (JSON array or comma-separated)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Optional metadata (JSON object)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// BIP44 account number (from derivation path m/44'/coin'/account'/change/index)
    /// </summary>
    public uint Account { get; set; }

    /// <summary>
    /// Navigation property to parent wallet
    /// </summary>
    public Wallet? Wallet { get; set; }
}
