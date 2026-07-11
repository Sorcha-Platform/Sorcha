// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Feature 181 US4 — durable persistence of a tenant's self-signed root CA (data-model §3, research R8).
/// Persists what <see cref="Sorcha.Tenant.Service.Trust.InternalCaTrustProvider"/> previously held only in
/// <c>ConcurrentDictionary</c>s (root DER, plaintext private key, CRL counter) so that issued-credential
/// x5c chains survive a restart. The CA private key is stored AES-256-GCM-encrypted at rest
/// (<see cref="PrivateKeyCiphertext"/> + <see cref="PrivateKeyNonce"/>) — constitution II.
/// </summary>
/// <remarks>
/// Named <c>*Record</c> to sit beside — not collide with — the existing
/// <see cref="Sorcha.Tenant.Models.Trust.TenantRootCa"/> transport DTO that
/// <see cref="Sorcha.Tenant.Service.Trust.ITrustProvider"/> returns; the write-through provider maps
/// between the two.
/// </remarks>
public sealed class TenantRootCaRecord
{
    /// <summary>Tenant that owns this root CA (primary key — one root per tenant).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>DER-encoded self-signed root certificate.</summary>
    public byte[] CertificateDer { get; set; } = [];

    /// <summary>AES-256-GCM ciphertext of the EC private key (raw <c>ExportECPrivateKey</c> bytes).</summary>
    public byte[] PrivateKeyCiphertext { get; set; } = [];

    /// <summary>96-bit AES-GCM nonce for <see cref="PrivateKeyCiphertext"/>.</summary>
    public byte[] PrivateKeyNonce { get; set; } = [];

    /// <summary>Certificate serial number (hex string).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>X.500 subject distinguished name.</summary>
    public string SubjectDn { get; set; } = string.Empty;

    /// <summary>Signing algorithm — <c>ES256</c> only (v1).</summary>
    public string Algorithm { get; set; } = "ES256";

    /// <summary>Certificate validity start.</summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>Certificate validity end.</summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>When the root CA was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Monotonic CRL number (RFC 5280 §5.2.3) — moves off the in-memory counter so it never
    /// regresses across restarts.</summary>
    public int CrlNumber { get; set; }
}

/// <summary>
/// Feature 181 US4 — provenance of an org certificate: an internally-issued tenant-root cert, or an
/// externally-issued cert imported by the admin (D4/D5).
/// </summary>
public enum OrgCertificateProvenance
{
    /// <summary>Signed by the tenant root CA (auto-enrol / re-issue).</summary>
    Internal = 0,

    /// <summary>Externally issued and uploaded by an org admin (US4).</summary>
    Imported = 1,
}

/// <summary>
/// Feature 181 US4 — lifecycle state of an org certificate. One <see cref="Active"/> per
/// (org, provenance); re-issue supersedes (FR-023d); <see cref="KeyMismatch"/> is set when a key rotation
/// invalidates an imported cert (edge case — FR-020).
/// </summary>
public enum OrgCertificateStatus
{
    /// <summary>The authoritative certificate for its (org, provenance).</summary>
    Active = 0,

    /// <summary>Replaced by a newer issue/import; retained for audit.</summary>
    Superseded = 1,

    /// <summary>Revoked (internal certs only — CRL path).</summary>
    Revoked = 2,

    /// <summary>The bound key no longer matches the org's resolved P-256 key after rotation.</summary>
    KeyMismatch = 3,
}

/// <summary>
/// Which of the org's keys the certificate binds (research R9). ED25519-primary orgs bind their derived
/// HAIP ES256 co-key; ES256-primary orgs bind the primary directly.
/// </summary>
public enum OrgCertificateKeySource
{
    /// <summary>The org wallet's primary signing key (ES256-primary orgs).</summary>
    Primary = 0,

    /// <summary>The org's HAIP classical ES256 co-key derived under <c>sorcha:haip-issuer-signing</c>.</summary>
    HaipCoKey = 1,
}

/// <summary>
/// Feature 181 US4 — a persisted org certificate, internal or imported (data-model §3). Persists what
/// <see cref="Sorcha.Tenant.Service.Trust.InternalCaTrustProvider"/> previously held only in memory and
/// adds imported-provenance rows carrying the full external chain.
/// </summary>
public sealed class OrgCertificateRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant that owns the org.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Organisation wallet address (with <see cref="TenantId"/>, the org identity).</summary>
    public string OrgWalletAddress { get; set; } = string.Empty;

    /// <summary>Internal (tenant-root) vs imported (external CA).</summary>
    public OrgCertificateProvenance Provenance { get; set; }

    /// <summary>Lifecycle state; one <see cref="OrgCertificateStatus.Active"/> per (org, provenance).</summary>
    public OrgCertificateStatus Status { get; set; } = OrgCertificateStatus.Active;

    /// <summary>DER-encoded leaf certificate.</summary>
    public byte[] CertificateDer { get; set; } = [];

    /// <summary>Ordered leaf-exclusive chain, DER per element (leaf→root). Internal ⇒ <c>[root]</c>.
    /// Serialised as jsonb (array of base64 strings).</summary>
    public List<byte[]> ChainDer { get; set; } = [];

    /// <summary>SPKI bytes of the P-256 key this cert binds (research R9). Import matches against it.</summary>
    public byte[] BoundPublicKeySpki { get; set; } = [];

    /// <summary>Which org key the cert binds.</summary>
    public OrgCertificateKeySource BoundKeySource { get; set; }

    /// <summary>Certificate serial number (hex string).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>X.500 subject distinguished name.</summary>
    public string SubjectDn { get; set; } = string.Empty;

    /// <summary>SAN — a DID URI (internal) or the certificate's SAN as issued (imported); display/audit.</summary>
    public string San { get; set; } = string.Empty;

    /// <summary>Certificate validity start.</summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>Certificate validity end (surfaced as the expiry warning in the admin UI).</summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>When the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Platform user who created it (system principal <see cref="Guid.Empty"/> for auto-enrol).</summary>
    public Guid CreatedByPlatformUserId { get; set; }

    /// <summary>When revoked (internal certs only; null otherwise).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Revocation reason (internal certs only).</summary>
    public string? RevocationReason { get; set; }
}

/// <summary>
/// Feature 181 US4 — a lightweight audit of a generated CSR (FR-018, data-model §3). The private key never
/// leaves Wallet Service; only the CSR PEM and the bound-key SPKI are recorded, so a later import can
/// confirm key continuity (<c>CERT_KEY_MISMATCH</c> otherwise).
/// </summary>
public sealed class CsrRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant that owns the org.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Organisation wallet address.</summary>
    public string OrgWalletAddress { get; set; } = string.Empty;

    /// <summary>The generated CSR, PEM-encoded (returned to the admin; stored for audit).</summary>
    public string CsrPem { get; set; } = string.Empty;

    /// <summary>SPKI bytes of the P-256 key the CSR binds — must match the imported certificate's key.</summary>
    public byte[] BoundPublicKeySpki { get; set; } = [];

    /// <summary>Which org key the CSR binds.</summary>
    public OrgCertificateKeySource BoundKeySource { get; set; }

    /// <summary>When the CSR was generated.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Platform user who generated it.</summary>
    public Guid CreatedByPlatformUserId { get; set; }
}
