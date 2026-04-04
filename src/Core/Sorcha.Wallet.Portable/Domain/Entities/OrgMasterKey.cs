// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Organisation master derivation key. One per organisation, holds the encrypted
/// master seed from which all participant keys are deterministically derived.
/// </summary>
public class OrgMasterKey
{
    /// <summary>
    /// Unique identifier for this master key record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Organisation that owns this master key.
    /// </summary>
    public required string OrganizationId { get; set; }

    /// <summary>
    /// Master seed encrypted at rest (AES-256-GCM via the configured protection provider).
    /// </summary>
    public required byte[] EncryptedSeed { get; set; }

    /// <summary>
    /// Key protection backend (e.g. "AzureKeyVault", "DPAPI", "HSM").
    /// </summary>
    public required string ProtectionProvider { get; set; }

    /// <summary>
    /// Identifier of the wrapping key within the protection provider.
    /// </summary>
    public required string ProtectionKeyId { get; set; }

    /// <summary>
    /// Cryptographic algorithm for derived keys (default ED25519).
    /// </summary>
    public required string Algorithm { get; set; } = "ED25519";

    /// <summary>
    /// Base64-encoded master public key (neutered extended key).
    /// </summary>
    public required string MasterPublicKey { get; set; }

    /// <summary>
    /// Lifecycle status of this master key.
    /// </summary>
    public OrgMasterKeyStatus Status { get; set; } = OrgMasterKeyStatus.Active;

    /// <summary>
    /// Timestamp when this master key was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this master key was rotated (replaced by a successor).
    /// </summary>
    public DateTime? RotatedAt { get; set; }

    /// <summary>
    /// Subject identifier of the user who created this master key.
    /// </summary>
    public required string CreatedBy { get; set; }

    /// <summary>
    /// Keys derived from this master key.
    /// </summary>
    public ICollection<DerivedKeyRecord> DerivedKeys { get; set; } = new List<DerivedKeyRecord>();
}
