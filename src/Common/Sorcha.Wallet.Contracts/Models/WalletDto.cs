// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for wallet information.
/// </summary>
public sealed record WalletDto
{
    /// <summary>Wallet address (public identifier).</summary>
    public required string Address { get; init; }

    /// <summary>Friendly name.</summary>
    public required string Name { get; init; }

    /// <summary>Public key (hex encoded).</summary>
    public required string PublicKey { get; init; }

    /// <summary>Cryptographic algorithm.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Wallet status (Active, Archived, Deleted, Locked).</summary>
    public required string Status { get; init; }

    /// <summary>Owner identifier.</summary>
    public required string Owner { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string Tenant { get; init; }

    /// <summary>Creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last update timestamp.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Signing mode: "Local" (keys stored encrypted locally) or "KmsResident" (keys held in cloud KMS).</summary>
    public string SigningMode { get; init; } = "Local";

    /// <summary>Cloud KMS key identifier. Null for Local wallets.</summary>
    public string? KmsKeyId { get; init; }

    /// <summary>Optional metadata.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}
