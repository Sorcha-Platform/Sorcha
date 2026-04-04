// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Stores a recovery key encrypted (wrapped) to a specific recipient's public key.
/// Each wrap enables one recovery path for one wallet.
/// </summary>
public class RecoveryKeyWrap
{
    /// <summary>
    /// Unique identifier for this wrap entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to Wallet.Address — the wallet this wrap belongs to.
    /// </summary>
    public required string WalletAddress { get; set; }

    /// <summary>
    /// Which recovery path this wrap enables.
    /// </summary>
    public RecoveryPathType RecoveryPath { get; set; }

    /// <summary>
    /// The recovery key encrypted to the recipient's public key (Base64).
    /// </summary>
    public required string EncryptedRecoveryKey { get; set; }

    /// <summary>
    /// Identifier of the public key used for wrapping.
    /// For Passkey: the passkey CredentialId (Base64).
    /// For OrgManaged: the org recovery key ID.
    /// </summary>
    public required string RecipientKeyId { get; set; }

    /// <summary>
    /// Asymmetric algorithm used for wrapping (e.g., "ED25519", "ES256").
    /// </summary>
    public required string Algorithm { get; set; }

    /// <summary>
    /// When this wrap was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this wrap was revoked (null = active).
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Navigation property to parent wallet.
    /// </summary>
    public Wallet? Wallet { get; set; }
}
