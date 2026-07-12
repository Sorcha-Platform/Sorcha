// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for a derived wallet address.
/// </summary>
public sealed record WalletAddressDto
{
    /// <summary>Unique identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Parent wallet address.</summary>
    public required string ParentWalletAddress { get; init; }

    /// <summary>The derived public address.</summary>
    public required string Address { get; init; }

    /// <summary>Public key (base64 encoded).</summary>
    public string? PublicKey { get; init; }

    /// <summary>BIP44 derivation path.</summary>
    public required string DerivationPath { get; init; }

    /// <summary>Address index in the derivation sequence.</summary>
    public int Index { get; init; }

    /// <summary>BIP44 account number.</summary>
    public uint Account { get; init; }

    /// <summary>Is this a change address?</summary>
    public bool IsChange { get; init; }

    /// <summary>Optional label.</summary>
    public string? Label { get; init; }

    /// <summary>Optional notes.</summary>
    public string? Notes { get; init; }

    /// <summary>Tags for categorization.</summary>
    public string? Tags { get; init; }

    /// <summary>Whether this address has been used.</summary>
    public bool IsUsed { get; init; }

    /// <summary>When address was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When address was first used.</summary>
    public DateTime? FirstUsedAt { get; init; }

    /// <summary>When address was last used.</summary>
    public DateTime? LastUsedAt { get; init; }

    /// <summary>Optional metadata.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}
