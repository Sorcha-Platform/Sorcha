// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Links a user to a deterministically derived key within an organisation's
/// key hierarchy. Tracks the derivation path, usage purpose, and custody mode.
/// </summary>
public class DerivedKeyRecord
{
    /// <summary>
    /// Unique identifier for this derived key record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the parent organisation master key.
    /// </summary>
    public required Guid OrgMasterKeyId { get; set; }

    /// <summary>
    /// Organisation that owns this derived key.
    /// </summary>
    public required string OrganizationId { get; set; }

    /// <summary>
    /// Subject identifier of the user this key is derived for.
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// Department index within the derivation hierarchy (default 0 for flat orgs).
    /// </summary>
    public uint DepartmentId { get; set; }

    /// <summary>
    /// Intended usage purpose of this derived key.
    /// </summary>
    public required KeyUsage KeyUsage { get; set; }

    /// <summary>
    /// Key index within the usage-scoped derivation level.
    /// </summary>
    public uint KeyIndex { get; set; }

    /// <summary>
    /// Full BIP-44-style derivation path (e.g. "m/44'/837'/0'/1/0").
    /// </summary>
    public required string DerivationPath { get; set; }

    /// <summary>
    /// Wallet address derived from the public key at this path.
    /// </summary>
    public required string WalletAddress { get; set; }

    /// <summary>
    /// Lifecycle status of this derived key.
    /// </summary>
    public DerivedKeyStatus Status { get; set; } = DerivedKeyStatus.Active;

    /// <summary>
    /// Key custody mode determining where key material is held.
    /// </summary>
    public CustodyMode CustodyMode { get; set; } = CustodyMode.Custodial;

    /// <summary>
    /// Timestamp when this derived key was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this derived key was revoked. Null if still active.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Navigation to the parent organisation master key.
    /// </summary>
    public OrgMasterKey OrgMasterKey { get; set; } = null!;

    /// <summary>
    /// Navigation to the wallet created from this derived key.
    /// </summary>
    public Domain.Entities.Wallet Wallet { get; set; } = null!;
}
