// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Trust;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Provisions and manages X.509 trust anchors and organisation certificates.
/// </summary>
public interface ITrustProvider
{
    /// <summary>
    /// Provisions a self-signed root CA for a tenant. Idempotent — returns
    /// the existing root if one is already provisioned.
    /// </summary>
    Task<TenantRootCa> ProvisionTrustAnchorAsync(
        string tenantId,
        string? subjectDn = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the trust anchor for a tenant, or null if not provisioned.
    /// </summary>
    Task<TenantRootCa?> GetTrustAnchorAsync(
        string tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Issues an organisation certificate signed by the tenant root CA.
    /// </summary>
    Task<OrgCertEnrolment> IssueOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        byte[] orgPublicKey,
        string orgDisplayName,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the cert chain (leaf + root) for a given org wallet.
    /// </summary>
    Task<(byte[] OrgCertDer, byte[] RootCertDer)?> GetOrgCertChainAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Feature 096 US4 — revokes an organisation certificate. Marks the
    /// enrolment with <see cref="OrgCertEnrolment.RevokedAt"/>, records the
    /// revocation reason, and regenerates the tenant CRL so future verifiers
    /// see the new serial. Idempotent — revoking an already-revoked cert is
    /// a no-op that returns the current enrolment.
    /// </summary>
    Task<OrgCertEnrolment> RevokeOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        string? reason = null,
        CancellationToken ct = default);

    /// <summary>
    /// Feature 096 US4 — returns the tenant's current CRL, regenerating it
    /// when the cached copy is past its <c>NextUpdate</c>. Returns null when
    /// the tenant has no provisioned root CA (no point publishing a CRL
    /// without a signer). Always regenerates after a revocation even if the
    /// cached copy is fresh — the revocation path calls this through
    /// <see cref="RevokeOrgCertAsync"/>.
    /// </summary>
    Task<TenantCrl?> GetOrPublishCrlAsync(
        string tenantId,
        CancellationToken ct = default);
}
