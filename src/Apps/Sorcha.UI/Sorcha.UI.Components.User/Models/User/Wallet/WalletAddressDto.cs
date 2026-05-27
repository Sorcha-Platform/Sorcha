// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Data transfer object for wallet address information
/// </summary>
public class WalletAddressDto
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Parent wallet address
    /// </summary>
    public required string ParentWalletAddress { get; set; }

    /// <summary>
    /// The derived public address
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// Public key (base64 encoded)
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>
    /// BIP44 derivation path
    /// </summary>
    public required string DerivationPath { get; set; }

    /// <summary>
    /// Address index in the derivation sequence
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// BIP44 account number
    /// </summary>
    public uint Account { get; set; }

    /// <summary>
    /// Is this a change address?
    /// </summary>
    public bool IsChange { get; set; }

    /// <summary>
    /// Optional label
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Optional notes
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Tags for categorization
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Whether this address has been used
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// When address was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When address was first used
    /// </summary>
    public DateTime? FirstUsedAt { get; set; }

    /// <summary>
    /// When address was last used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Optional metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}
