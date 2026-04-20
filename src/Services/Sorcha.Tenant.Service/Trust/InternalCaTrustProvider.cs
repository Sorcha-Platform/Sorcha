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
    private readonly ConcurrentDictionary<string, TenantCrl> _crls = new();
    // Monotonic CRL version counter per tenant — RFC 5280 requires crlNumber to
    // strictly increase across all CRLs issued under a given CA, independent of
    // whether the cache still holds the previous copy.
    private readonly ConcurrentDictionary<string, int> _crlCounters = new();
    private readonly ILogger<InternalCaTrustProvider> _logger;
    private readonly string _defaultAlgorithm;
    private readonly int _defaultCaValidityYears;
    private readonly int _defaultOrgCertValidityYears;
    private readonly int _crlRefreshHours;
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
        _crlRefreshHours = configuration.GetValue<int>("Trust:CrlRefreshHours", 24);
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

        var now = DateTimeOffset.UtcNow;
        var (certDer, privateKey, serialNumber) = X509CertificateBuilder.BuildSelfSignedRoot(
            _defaultAlgorithm, dn, _defaultCaValidityYears);

        var rootCa = new TenantRootCa
        {
            TenantId = tenantId,
            CertificateDer = certDer,
            SerialNumber = serialNumber,
            SubjectDn = dn,
            Algorithm = _defaultAlgorithm,
            NotBefore = now,
            NotAfter = now.AddYears(_defaultCaValidityYears),
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

    /// <inheritdoc />
    public Task<OrgCertEnrolment> RevokeOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        string? reason = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);

        var key = $"{tenantId}:{orgWalletAddress}";
        if (!_orgCerts.TryGetValue(key, out var enrolment))
            throw new InvalidOperationException(
                $"No org cert to revoke for {orgWalletAddress} under tenant {tenantId}");

        // Idempotent — already revoked returns current state.
        if (enrolment.RevokedAt != null)
        {
            _logger.LogInformation(
                "Org cert {Serial} for {OrgWallet} already revoked at {RevokedAt}",
                enrolment.SerialNumber, orgWalletAddress, enrolment.RevokedAt);
            return Task.FromResult(enrolment);
        }

        enrolment.RevokedAt = DateTimeOffset.UtcNow;
        enrolment.RevocationReason = reason;
        _orgCerts[key] = enrolment;

        // Force CRL regeneration so the next fetch sees the new serial. Clearing
        // the cache is the minimum — GetOrPublishCrlAsync rebuilds on miss.
        _crls.TryRemove(tenantId, out _);

        _logger.LogInformation(
            "Revoked org cert {Serial} for {OrgWallet} under tenant {TenantId}: {Reason}",
            enrolment.SerialNumber, orgWalletAddress, tenantId, reason ?? "(no reason)");

        return Task.FromResult(enrolment);
    }

    /// <inheritdoc />
    public Task<TenantCrl?> GetOrPublishCrlAsync(string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!_roots.TryGetValue(tenantId, out var rootCa))
            return Task.FromResult<TenantCrl?>(null);
        if (!_rootPrivateKeys.TryGetValue(tenantId, out var rootPrivateKey))
            return Task.FromResult<TenantCrl?>(null);

        // Use cached copy if still fresh.
        if (_crls.TryGetValue(tenantId, out var cached)
            && cached.NextUpdate > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<TenantCrl?>(cached);
        }

        // Collect all revoked serials for this tenant.
        var revoked = _orgCerts.Values
            .Where(e => e.TenantId == tenantId && e.RevokedAt.HasValue)
            .Select(e => (e.SerialNumber, e.RevokedAt!.Value))
            .ToList();

        var crlNumber = _crlCounters.AddOrUpdate(tenantId, 1, (_, prev) => prev + 1);
        var (crlDer, nextUpdate) = TenantCrlBuilder.Build(
            rootCa.CertificateDer, rootPrivateKey, revoked, crlNumber, _crlRefreshHours, rootCa.Algorithm);

        var crl = new TenantCrl
        {
            TenantId = tenantId,
            CrlDer = crlDer,
            Version = crlNumber,
            LastUpdated = DateTimeOffset.UtcNow,
            NextUpdate = nextUpdate,
        };
        _crls[tenantId] = crl;

        _logger.LogInformation(
            "Published CRL v{Version} for tenant {TenantId} with {Count} revoked entries (next update {NextUpdate})",
            crlNumber, tenantId, revoked.Count, nextUpdate);

        return Task.FromResult<TenantCrl?>(crl);
    }
}
