// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Encrypted per-user persona blob. One row per
/// (<see cref="PlatformUser"/>, <see cref="ContextOrgId"/>) pair — Feature 092
/// gave the user a single persona; Feature 125 added per-context scoping so a
/// citizen can hold one persona per organisational context (Personal +
/// each org they hold an OrgMembership for). Created lazily on the first
/// <c>PUT /me/persona?context=&lt;orgId&gt;</c>. Hard-deleted atomically with
/// the owning <see cref="PlatformUser"/> via an EF cascade rule.
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
/// <para>
/// <see cref="ContextOrgId"/> is the organisation the persona is scoped to.
/// <see cref="Guid.Empty"/> represents the Personal context (the wire-side
/// API uses null in the <c>?context=</c> query parameter for the same
/// meaning). The encryption envelope is unchanged across contexts — the
/// per-context scoping is row discrimination only, not key derivation.
/// </para>
/// </remarks>
public class PlatformUserPersona
{
    /// <summary>
    /// First half of the composite primary key — foreign key to
    /// <see cref="PlatformUser.Id"/>.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Second half of the composite primary key — organisation scope for the
    /// persona. <see cref="Guid.Empty"/> = Personal context (the API
    /// represents this as null in the <c>?context=</c> query parameter).
    /// </summary>
    public Guid ContextOrgId { get; set; } = Guid.Empty;

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
