// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Represents a cryptographic wallet with encrypted private keys
/// </summary>
public class Wallet
{
    /// <summary>
    /// Primary wallet address (public key hash)
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// Encrypted private key (AES-256-GCM)
    /// </summary>
    public required string EncryptedPrivateKey { get; set; }

    /// <summary>
    /// Reference to the encryption key used (Key Vault ID or local key identifier)
    /// </summary>
    public required string EncryptionKeyId { get; set; }

    /// <summary>
    /// Cryptographic algorithm (ED25519, SECP256K1, RSA)
    /// </summary>
    public required string Algorithm { get; set; }

    /// <summary>
    /// Owner subject identifier (user ID from auth system)
    /// </summary>
    public required string Owner { get; set; }

    /// <summary>
    /// Tenant identifier for multi-tenancy
    /// </summary>
    public required string Tenant { get; set; }

    /// <summary>
    /// Friendly name for the wallet
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Public key (base64 encoded)
    /// </summary>
    public string? PublicKey { get; set; }

    /// <summary>
    /// Custom metadata tags (key-value pairs)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Custom tags (legacy - use Metadata instead)
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }

    /// <summary>
    /// Current wallet status
    /// </summary>
    public WalletStatus Status { get; set; } = WalletStatus.Active;

    /// <summary>
    /// Timestamp when wallet was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of last modification
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when wallet was last accessed
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when wallet was soft-deleted
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Derived addresses from this wallet
    /// </summary>
    public ICollection<WalletAddress> Addresses { get; set; } = new List<WalletAddress>();

    /// <summary>
    /// Access control entries (delegations)
    /// </summary>
    public ICollection<WalletAccess> Delegates { get; set; } = new List<WalletAccess>();

    /// <summary>
    /// Transaction history
    /// </summary>
    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();

    /// <summary>
    /// Schema version for migrations
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Concurrency token for optimistic locking
    /// </summary>
    public byte[]? RowVersion { get; set; }
}
