// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Trust;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// In-memory trust provider that provisions self-signed root CAs and
/// issues organisation certificates. Production would persist to PostgreSQL.
/// </summary>
public class InternalCaTrustProvider : ITrustProvider
{
    private readonly ConcurrentDictionary<string, TenantRootCa> _roots = new();
    private readonly ConcurrentDictionary<string, byte[]> _rootPrivateKeys = new();
    private readonly ConcurrentDictionary<string, OrgCertEnrolment> _orgCerts = new();
    private readonly ILogger<InternalCaTrustProvider> _logger;
    private readonly string _defaultAlgorithm;
    private readonly int _defaultCaValidityYears;
    private readonly int _defaultOrgCertValidityYears;
    private readonly string _trustBaseUrl;

    /// <summary>
    /// Initialises a new instance of the <see cref="InternalCaTrustProvider"/> class.
    /// </summary>
    public InternalCaTrustProvider(
        ILogger<InternalCaTrustProvider> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultAlgorithm = configuration.GetValue<string>("Trust:DefaultCaAlgorithm") ?? "ES256";
        _defaultCaValidityYears = configuration.GetValue<int>("Trust:DefaultCaValidityYears", 10);
        _defaultOrgCertValidityYears = configuration.GetValue<int>("Trust:DefaultOrgCertValidityYears", 3);
        _trustBaseUrl = configuration.GetValue<string>("Trust:BaseUrl")
            ?? "https://sorcha.example/api/v1/trust";
    }

    /// <inheritdoc />
    public Task<TenantRootCa> ProvisionTrustAnchorAsync(
        string tenantId,
        string? subjectDn = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Idempotent — return existing root if provisioned
        if (_roots.TryGetValue(tenantId, out var existing))
        {
            _logger.LogInformation(
                "Trust anchor already provisioned for tenant {TenantId}, returning existing",
                tenantId);
            return Task.FromResult(existing);
        }

        var dn = subjectDn ?? $"CN=Sorcha Tenant {tenantId} Root CA, O=Sorcha, C=IE";

        var (certDer, privateKey, serialNumber) = X509CertificateBuilder.BuildSelfSignedRoot(
            _defaultAlgorithm, dn, _defaultCaValidityYears);

        var rootCa = new TenantRootCa
        {
            TenantId = tenantId,
            CertificateDer = certDer,
            SerialNumber = serialNumber,
            SubjectDn = dn,
            Algorithm = _defaultAlgorithm,
            NotBefore = DateTimeOffset.UtcNow,
            NotAfter = DateTimeOffset.UtcNow.AddYears(_defaultCaValidityYears),
            KeyProtectionMode = TrustProviderMode.Local
        };

        if (_roots.TryAdd(tenantId, rootCa))
        {
            _rootPrivateKeys[tenantId] = privateKey;

            _logger.LogInformation(
                "Provisioned trust anchor for tenant {TenantId}: serial={Serial}, algorithm={Algorithm}, valid until {NotAfter}",
                tenantId, serialNumber, _defaultAlgorithm, rootCa.NotAfter);
        }
        else
        {
            // Race — another thread provisioned first
            rootCa = _roots[tenantId];
        }

        return Task.FromResult(rootCa);
    }

    /// <inheritdoc />
    public Task<TenantRootCa?> GetTrustAnchorAsync(string tenantId, CancellationToken ct = default)
    {
        _roots.TryGetValue(tenantId, out var root);
        return Task.FromResult(root);
    }

    /// <inheritdoc />
    public Task<OrgCertEnrolment> IssueOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        byte[] orgPublicKey,
        string orgDisplayName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);
        ArgumentNullException.ThrowIfNull(orgPublicKey);

        if (!_roots.TryGetValue(tenantId, out var rootCa))
            throw new InvalidOperationException(
                $"Tenant {tenantId} has no provisioned trust anchor. Call ProvisionTrustAnchorAsync first.");

        if (!_rootPrivateKeys.TryGetValue(tenantId, out var rootPrivateKey))
            throw new InvalidOperationException(
                $"Root CA private key not available for tenant {tenantId}.");

        var key = $"{tenantId}:{orgWalletAddress}";
        if (_orgCerts.TryGetValue(key, out var existing) && existing.RevokedAt == null)
        {
            _logger.LogInformation(
                "Org cert already issued for {OrgWallet} under tenant {TenantId}",
                orgWalletAddress, tenantId);
            return Task.FromResult(existing);
        }

        var subjectDn = $"CN={orgDisplayName}, O=Sorcha Org, C=IE";
        var sanUri = $"did:sorcha:org:{orgWalletAddress}";
        var crlDp = $"{_trustBaseUrl}/tenants/{tenantId}/crl";

        var (certDer, serialNumber) = X509CertificateBuilder.BuildOrgCert(
            rootCa.CertificateDer, rootPrivateKey, orgPublicKey,
            subjectDn, sanUri, crlDp, _defaultOrgCertValidityYears);

        var enrolment = new OrgCertEnrolment
        {
            TenantId = tenantId,
            OrgWalletAddress = orgWalletAddress,
            CertificateDer = certDer,
            SerialNumber = serialNumber,
            SubjectDn = subjectDn,
            SanUri = sanUri,
            NotBefore = DateTimeOffset.UtcNow,
            NotAfter = DateTimeOffset.UtcNow.AddYears(_defaultOrgCertValidityYears)
        };

        _orgCerts[key] = enrolment;

        _logger.LogInformation(
            "Issued org cert for {OrgWallet} under tenant {TenantId}: serial={Serial}",
            orgWalletAddress, tenantId, serialNumber);

        return Task.FromResult(enrolment);
    }

    /// <inheritdoc />
    public Task<(byte[] OrgCertDer, byte[] RootCertDer)?> GetOrgCertChainAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default)
    {
        var key = $"{tenantId}:{orgWalletAddress}";
        if (!_orgCerts.TryGetValue(key, out var enrolment) || enrolment.RevokedAt != null)
            return Task.FromResult<(byte[], byte[])?>(null);

        if (!_roots.TryGetValue(tenantId, out var rootCa))
            return Task.FromResult<(byte[], byte[])?>(null);

        return Task.FromResult<(byte[], byte[])?>(
            (enrolment.CertificateDer, rootCa.CertificateDer));
    }
}
