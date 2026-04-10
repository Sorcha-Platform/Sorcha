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
}
