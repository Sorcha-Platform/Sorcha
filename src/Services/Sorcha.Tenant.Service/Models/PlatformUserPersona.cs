// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Encrypted per-user persona blob. Exactly one row per
/// <see cref="PlatformUser"/>. Created lazily on the first
/// <c>PUT /me/persona</c>. Hard-deleted atomically with the owning
/// <see cref="PlatformUser"/> via an EF cascade rule.
/// </summary>
/// <remarks>
/// <para>
/// The ciphertext lives here in the Tenant Service database. The encryption
/// key is derived on demand by the Wallet Service under the
/// <c>sorcha:persona-vault</c> derivation purpose. The key and the ciphertext
/// are deliberately not co-located in a single service.
/// </para>
/// <para>
/// <see cref="WrappedKeyRef"/> is opaque to the Tenant Service — it is a
/// handle that the Wallet Service uses to locate the per-user wrapped content
/// key. In v1 it is equal to the owning user's primary wallet address; the
/// column exists for forward compatibility with per-recipient key wrapping
/// when wallet delegation (Power of Attorney) lands.
/// </para>
/// </remarks>
public class PlatformUserPersona
{
    /// <summary>
    /// Primary key and foreign key to <see cref="PlatformUser.Id"/>.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// XChaCha20-Poly1305 ciphertext of the serialised
    /// <c>PersonaAttributesV1</c> JSON document (includes the Poly1305
    /// authentication tag).
    /// </summary>
    public byte[] CiphertextBlob { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 24-byte nonce used for this ciphertext.
    /// </summary>
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Opaque handle used by the Wallet Service to unwrap the content key.
    /// In v1, equal to the owning user's primary wallet address.
    /// </summary>
    public string WrappedKeyRef { get; set; } = string.Empty;

    /// <summary>
    /// Schema version of the serialised plaintext. Always 1 in this feature.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// When the persona row was first created. Stored as PostgreSQL
    /// <c>timestamptz</c> via <see cref="DateTimeOffset"/> so the kind is
    /// unambiguous across reads and writes.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the persona row was last updated. Stored as PostgreSQL
    /// <c>timestamptz</c>.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to the owning <see cref="PlatformUser"/>.
    /// </summary>
    public PlatformUser? PlatformUser { get; set; }
}
