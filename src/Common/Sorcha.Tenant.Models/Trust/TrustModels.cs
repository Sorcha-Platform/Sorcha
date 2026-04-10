// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Models.Trust;

/// <summary>
/// Represents a tenant-level self-signed root CA certificate.
/// One per tenant — acts as the trust anchor for all organisation certificates
/// issued under that tenant.
/// </summary>
public class TenantRootCa
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant that owns this root CA.</summary>
    public required string TenantId { get; set; }

    /// <summary>DER-encoded self-signed root certificate.</summary>
    public required byte[] CertificateDer { get; set; }

    /// <summary>Certificate serial number (hex string).</summary>
    public required string SerialNumber { get; set; }

    /// <summary>X.500 subject distinguished name.</summary>
    public required string SubjectDn { get; set; }

    /// <summary>Signing algorithm used (e.g., "ES256", "EdDSA").</summary>
    public required string Algorithm { get; set; }

    /// <summary>Certificate validity start.</summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>Certificate validity end.</summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>When the root CA was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>How the private key is protected.</summary>
    public TrustProviderMode KeyProtectionMode { get; set; } = TrustProviderMode.Local;
}

/// <summary>
/// Represents an organisation certificate issued by the tenant root CA.
/// Binds the organisation's HAIP classical co-key to an X.509 identity.
/// </summary>
public class OrgCertEnrolment
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant that issued the cert.</summary>
    public required string TenantId { get; set; }

    /// <summary>Organisation wallet address.</summary>
    public required string OrgWalletAddress { get; set; }

    /// <summary>DER-encoded organisation certificate.</summary>
    public required byte[] CertificateDer { get; set; }

    /// <summary>Certificate serial number (hex string).</summary>
    public required string SerialNumber { get; set; }

    /// <summary>X.500 subject distinguished name.</summary>
    public required string SubjectDn { get; set; }

    /// <summary>SAN URI (e.g., "did:sorcha:org:{walletAddress}").</summary>
    public required string SanUri { get; set; }

    /// <summary>Certificate validity start.</summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>Certificate validity end.</summary>
    public DateTimeOffset NotAfter { get; set; }

    /// <summary>When the cert was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the cert was revoked (null if active).</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Revocation reason (null if active).</summary>
    public string? RevocationReason { get; set; }
}

/// <summary>
/// Represents the tenant's Certificate Revocation List (CRL).
/// Regenerated whenever an org cert is revoked.
/// </summary>
public class TenantCrl
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant that owns this CRL.</summary>
    public required string TenantId { get; set; }

    /// <summary>DER-encoded CRL.</summary>
    public required byte[] CrlDer { get; set; }

    /// <summary>CRL version (incremented on each regeneration).</summary>
    public int Version { get; set; } = 1;

    /// <summary>When the CRL was last regenerated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the CRL expires (next scheduled regeneration).</summary>
    public DateTimeOffset NextUpdate { get; set; }
}

/// <summary>
/// How the trust anchor's private key is managed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustProviderMode
{
    /// <summary>Key stored locally (encrypted by the Tenant Service key protection).</summary>
    Local = 0,

    /// <summary>Key managed by a cloud KMS (Azure Key Vault, AWS KMS, etc.).</summary>
    KmsResident = 1,

    /// <summary>Trust anchor imported from an external CA — no local key.</summary>
    External = 2
}
