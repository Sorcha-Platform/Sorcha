// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Models.Trust;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Storage;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US4 — write-through trust provider that provisions self-signed root CAs and
/// issues organisation certificates. The authoritative store is the EF-backed
/// <see cref="ICertificateStore"/> (tenant root CAs, their encrypted private keys, the CRL
/// counter, and internal org certs), so state now survives a restart (research R8). Small
/// in-memory <see cref="ConcurrentDictionary{TKey,TValue}"/> caches sit in front of the store
/// as a read-through / write-through cache — the store is the source of truth.
/// </summary>
/// <remarks>
/// Registered as a singleton, so the scoped <see cref="ICertificateStore"/> (an EF DbContext) is
/// resolved inside a per-operation scope via <see cref="IServiceScopeFactory"/>. The singleton
/// <see cref="ISecretProtectionProvider"/> is injected directly and encrypts / decrypts the root
/// CA private key at rest.
/// </remarks>
public class InternalCaTrustProvider : ITrustProvider
{
    // Read-through / write-through caches. The ICertificateStore is authoritative; these avoid a
    // round-trip on the hot path and preserve stable DTO identities within a process lifetime.
    private readonly ConcurrentDictionary<string, TenantRootCa> _roots = new();
    private readonly ConcurrentDictionary<string, byte[]> _rootPrivateKeys = new();
    private readonly ConcurrentDictionary<string, OrgCertEnrolment> _orgCerts = new();
    private readonly ConcurrentDictionary<string, TenantCrl> _crls = new();

    private readonly ILogger<InternalCaTrustProvider> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecretProtectionProvider _secretProtection;
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
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ISecretProtectionProvider secretProtection)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _secretProtection = secretProtection ?? throw new ArgumentNullException(nameof(secretProtection));
        _defaultAlgorithm = configuration.GetValue<string>("Trust:DefaultCaAlgorithm") ?? "ES256";
        _defaultCaValidityYears = configuration.GetValue<int>("Trust:DefaultCaValidityYears", 10);
        _defaultOrgCertValidityYears = configuration.GetValue<int>("Trust:DefaultOrgCertValidityYears", 3);
        _crlRefreshHours = configuration.GetValue<int>("Trust:CrlRefreshHours", 24);
        _trustBaseUrl = configuration.GetValue<string>("Trust:BaseUrl")
            ?? "https://sorcha.example/api/v1/trust";
    }

    /// <inheritdoc />
    public async Task<TenantRootCa> ProvisionTrustAnchorAsync(
        string tenantId,
        string? subjectDn = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Idempotent — return the cached DTO if we already provisioned in this process.
        if (_roots.TryGetValue(tenantId, out var cached))
        {
            _logger.LogInformation(
                "Trust anchor already provisioned for tenant {TenantId}, returning existing",
                tenantId);
            return cached;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        // Idempotent — return the persisted root if one already exists (survives restart).
        var existingRecord = await store.GetRootAsync(tenantId, ct);
        if (existingRecord is not null)
        {
            _logger.LogInformation(
                "Trust anchor already persisted for tenant {TenantId}, hydrating from store",
                tenantId);
            return CacheRoot(MapRoot(existingRecord));
        }

        var dn = subjectDn ?? $"CN=Sorcha Tenant {tenantId} Root CA, O=Sorcha, C=IE";

        var now = DateTimeOffset.UtcNow;
        var (certDer, privateKey, serialNumber) = X509CertificateBuilder.BuildSelfSignedRoot(
            _defaultAlgorithm, dn, _defaultCaValidityYears);

        // Encrypt the CA private key at rest. ISecretProtectionProvider.EncryptAsync returns a
        // self-framed ciphertext envelope (nonce ∥ ciphertext ∥ tag) plus the id of the protecting
        // key. The entity's PrivateKeyNonce column is REPURPOSED to carry the KeyId (UTF-8 bytes) —
        // the nonce already lives inside the envelope, so the raw-nonce column would otherwise be
        // unused. Decrypt reads the KeyId back via Encoding.UTF8.GetString(record.PrivateKeyNonce).
        var (ciphertext, keyId) = await _secretProtection.EncryptAsync(privateKey, ct);

        var record = new TenantRootCaRecord
        {
            TenantId = tenantId,
            CertificateDer = certDer,
            PrivateKeyCiphertext = ciphertext,
            PrivateKeyNonce = Encoding.UTF8.GetBytes(keyId),
            SerialNumber = serialNumber,
            SubjectDn = dn,
            Algorithm = _defaultAlgorithm,
            NotBefore = now,
            NotAfter = now.AddYears(_defaultCaValidityYears),
            CreatedAt = now,
            CrlNumber = 0,
        };

        await store.AddRootAsync(record, ct);

        // Re-read so the cache mirrors the canonical persisted row (AddRootAsync is a no-op if a
        // concurrent caller won the race) and cache the decrypted key against that row.
        var persisted = await store.GetRootAsync(tenantId, ct) ?? record;
        _rootPrivateKeys[tenantId] = await DecryptRootKeyAsync(persisted, ct);

        _logger.LogInformation(
            "Provisioned trust anchor for tenant {TenantId}: serial={Serial}, algorithm={Algorithm}, valid until {NotAfter}",
            tenantId, persisted.SerialNumber, _defaultAlgorithm, persisted.NotAfter);

        return CacheRoot(MapRoot(persisted));
    }

    /// <inheritdoc />
    public async Task<TenantRootCa?> GetTrustAnchorAsync(string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_roots.TryGetValue(tenantId, out var cached))
            return cached;

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        var record = await store.GetRootAsync(tenantId, ct);
        return record is null ? null : CacheRoot(MapRoot(record));
    }

    /// <inheritdoc />
    public async Task<OrgCertEnrolment> IssueOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        byte[] orgPublicKey,
        string orgDisplayName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);
        ArgumentNullException.ThrowIfNull(orgPublicKey);

        var cacheKey = OrgKey(tenantId, orgWalletAddress);

        // Idempotent — return the existing enrolment if it is not revoked.
        if (_orgCerts.TryGetValue(cacheKey, out var cachedCert) && cachedCert.RevokedAt == null)
        {
            _logger.LogInformation(
                "Org cert already issued for {OrgWallet} under tenant {TenantId}",
                orgWalletAddress, tenantId);
            return cachedCert;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        var rootRecord = await store.GetRootAsync(tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Tenant {tenantId} has no provisioned trust anchor. Call ProvisionTrustAnchorAsync first.");

        // Persisted idempotency (survives restart): an Active internal cert already exists.
        var existingActive = await store.GetActiveAsync(
            tenantId, orgWalletAddress, OrgCertificateProvenance.Internal, ct);
        if (existingActive is not null)
        {
            _logger.LogInformation(
                "Org cert already issued for {OrgWallet} under tenant {TenantId}",
                orgWalletAddress, tenantId);
            return CacheOrgCert(cacheKey, MapOrgCert(existingActive));
        }

        var rootPrivateKey = await GetRootPrivateKeyAsync(rootRecord, ct);

        var subjectDn = $"CN={orgDisplayName}, O=Sorcha Org, C=IE";
        var sanUri = $"did:sorcha:org:{orgWalletAddress}";
        var crlDp = $"{_trustBaseUrl}/tenants/{tenantId}/crl";

        var (certDer, serialNumber) = X509CertificateBuilder.BuildOrgCert(
            rootRecord.CertificateDer, rootPrivateKey, orgPublicKey,
            subjectDn, sanUri, crlDp, _defaultOrgCertValidityYears);

        var now = DateTimeOffset.UtcNow;
        var record = new OrgCertificateRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrgWalletAddress = orgWalletAddress,
            Provenance = OrgCertificateProvenance.Internal,
            Status = OrgCertificateStatus.Active,
            CertificateDer = certDer,
            ChainDer = [rootRecord.CertificateDer],
            BoundPublicKeySpki = orgPublicKey,
            BoundKeySource = OrgCertificateKeySource.HaipCoKey,
            SerialNumber = serialNumber,
            SubjectDn = subjectDn,
            San = sanUri,
            NotBefore = now,
            NotAfter = now.AddYears(_defaultOrgCertValidityYears),
            CreatedAt = now,
            CreatedByPlatformUserId = Guid.Empty,
        };

        await store.AddAsync(record, ct);

        _logger.LogInformation(
            "Issued org cert for {OrgWallet} under tenant {TenantId}: serial={Serial}",
            orgWalletAddress, tenantId, serialNumber);

        return CacheOrgCert(cacheKey, MapOrgCert(record));
    }

    /// <inheritdoc />
    public async Task<OrgCertEnrolment> ReissueInternalCertAsync(
        string tenantId,
        string orgWalletAddress,
        byte[] orgP256Spki,
        string orgDisplayName,
        OrgCertificateKeySource boundKeySource,
        Guid createdBy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);
        ArgumentNullException.ThrowIfNull(orgP256Spki);

        // Ensure the tenant trust anchor exists (idempotent).
        await ProvisionTrustAnchorAsync(tenantId, ct: ct);

        var cacheKey = OrgKey(tenantId, orgWalletAddress);
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        var rootRecord = await store.GetRootAsync(tenantId, ct)
            ?? throw new InvalidOperationException(
                $"Tenant {tenantId} has no provisioned trust anchor. Call ProvisionTrustAnchorAsync first.");

        // Supersede any existing Active internal cert (auditable history, FR-023d).
        var existingActive = await store.GetActiveAsync(
            tenantId, orgWalletAddress, OrgCertificateProvenance.Internal, ct);
        if (existingActive is not null)
        {
            existingActive.Status = OrgCertificateStatus.Superseded;
            await store.UpdateAsync(existingActive, ct);
        }

        var rootPrivateKey = await GetRootPrivateKeyAsync(rootRecord, ct);

        var subjectDn = $"CN={orgDisplayName}, O=Sorcha Org, C=IE";
        var sanUri = $"did:sorcha:org:{orgWalletAddress}";
        var crlDp = $"{_trustBaseUrl}/tenants/{tenantId}/crl";

        // BuildOrgCert throws a typed CertKeyNotEligibleException if the key is not P-256 (T047).
        var (certDer, serialNumber) = X509CertificateBuilder.BuildOrgCert(
            rootRecord.CertificateDer, rootPrivateKey, orgP256Spki,
            subjectDn, sanUri, crlDp, _defaultOrgCertValidityYears);

        var now = DateTimeOffset.UtcNow;
        var record = new OrgCertificateRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrgWalletAddress = orgWalletAddress,
            Provenance = OrgCertificateProvenance.Internal,
            Status = OrgCertificateStatus.Active,
            CertificateDer = certDer,
            ChainDer = [rootRecord.CertificateDer],
            BoundPublicKeySpki = orgP256Spki,
            BoundKeySource = boundKeySource,
            SerialNumber = serialNumber,
            SubjectDn = subjectDn,
            San = sanUri,
            NotBefore = now,
            NotAfter = now.AddYears(_defaultOrgCertValidityYears),
            CreatedAt = now,
            CreatedByPlatformUserId = createdBy,
        };
        await store.AddAsync(record, ct);

        _logger.LogInformation(
            "(Re)issued internal cert for {OrgWallet} under tenant {TenantId}: serial={Serial} boundKeySource={Source} superseded={Superseded}",
            orgWalletAddress, tenantId, serialNumber, boundKeySource, existingActive is not null);

        return CacheOrgCert(cacheKey, MapOrgCert(record));
    }

    /// <inheritdoc />
    public async Task<(byte[] OrgCertDer, byte[] RootCertDer)?> GetOrgCertChainAsync(
        string tenantId,
        string orgWalletAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        // Only an Active internal cert is served — revoked / superseded rows return null.
        var leaf = await store.GetActiveAsync(
            tenantId, orgWalletAddress, OrgCertificateProvenance.Internal, ct);
        if (leaf is null)
            return null;

        // Prefer the chain stored on the cert (leaf-exclusive: [root]); fall back to the live root.
        var rootDer = leaf.ChainDer.Count > 0
            ? leaf.ChainDer[0]
            : (await store.GetRootAsync(tenantId, ct))?.CertificateDer;
        if (rootDer is null || rootDer.Length == 0)
            return null;

        return (leaf.CertificateDer, rootDer);
    }

    /// <inheritdoc />
    public async Task<OrgCertEnrolment> RevokeOrgCertAsync(
        string tenantId,
        string orgWalletAddress,
        string? reason = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgWalletAddress);

        var cacheKey = OrgKey(tenantId, orgWalletAddress);

        // Idempotent — an already-revoked cert returns its current (frozen) state.
        if (_orgCerts.TryGetValue(cacheKey, out var cachedCert) && cachedCert.RevokedAt != null)
        {
            _logger.LogInformation(
                "Org cert {Serial} for {OrgWallet} already revoked at {RevokedAt}",
                cachedCert.SerialNumber, orgWalletAddress, cachedCert.RevokedAt);
            return cachedCert;
        }

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        var active = await store.GetActiveAsync(
            tenantId, orgWalletAddress, OrgCertificateProvenance.Internal, ct);
        if (active is null)
        {
            // No Active internal cert — either never enrolled (throw) or already revoked (idempotent).
            var all = await store.ListForOrgAsync(tenantId, orgWalletAddress, ct);
            var internalCert = all.FirstOrDefault(c => c.Provenance == OrgCertificateProvenance.Internal);
            if (internalCert is null)
                throw new InvalidOperationException(
                    $"No org cert to revoke for {orgWalletAddress} under tenant {tenantId}");

            if (internalCert.RevokedAt != null)
            {
                _logger.LogInformation(
                    "Org cert {Serial} for {OrgWallet} already revoked at {RevokedAt}",
                    internalCert.SerialNumber, orgWalletAddress, internalCert.RevokedAt);
                return CacheOrgCert(cacheKey, MapOrgCert(internalCert));
            }

            active = internalCert;
        }

        active.Status = OrgCertificateStatus.Revoked;
        active.RevokedAt = DateTimeOffset.UtcNow;
        active.RevocationReason = reason;
        await store.UpdateAsync(active, ct);

        // Force CRL regeneration so the next fetch sees the new serial. Clearing the cache is the
        // minimum — GetOrPublishCrlAsync rebuilds on miss.
        _crls.TryRemove(tenantId, out _);

        _logger.LogInformation(
            "Revoked org cert {Serial} for {OrgWallet} under tenant {TenantId}: {Reason}",
            active.SerialNumber, orgWalletAddress, tenantId, reason ?? "(no reason)");

        return CacheOrgCert(cacheKey, MapOrgCert(active));
    }

    /// <inheritdoc />
    public async Task<TenantCrl?> GetOrPublishCrlAsync(string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICertificateStore>();

        var rootRecord = await store.GetRootAsync(tenantId, ct);
        if (rootRecord is null)
            return null;

        // Use cached copy if still fresh.
        if (_crls.TryGetValue(tenantId, out var cachedCrl)
            && cachedCrl.NextUpdate > DateTimeOffset.UtcNow)
        {
            return cachedCrl;
        }

        var rootPrivateKey = await GetRootPrivateKeyAsync(rootRecord, ct);

        // Collect all revoked internal-cert serials for this tenant from the write-through cache.
        var revoked = _orgCerts.Values
            .Where(e => e.TenantId == tenantId && e.RevokedAt.HasValue)
            .Select(e => (e.SerialNumber, e.RevokedAt!.Value))
            .ToList();

        // Monotonic CRL number from the store (RFC 5280 §5.2.3) — never regresses across restarts.
        var crlNumber = await store.NextCrlNumberAsync(tenantId, ct);
        var (crlDer, nextUpdate) = TenantCrlBuilder.Build(
            rootRecord.CertificateDer, rootPrivateKey, revoked, crlNumber, _crlRefreshHours, rootRecord.Algorithm);

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

        return crl;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string OrgKey(string tenantId, string orgWalletAddress) => $"{tenantId}:{orgWalletAddress}";

    private TenantRootCa CacheRoot(TenantRootCa dto)
    {
        _roots[dto.TenantId] = dto;
        return dto;
    }

    private OrgCertEnrolment CacheOrgCert(string cacheKey, OrgCertEnrolment dto)
    {
        _orgCerts[cacheKey] = dto;
        return dto;
    }

    /// <summary>Decrypts and caches the root CA private key for the given persisted record.</summary>
    private async Task<byte[]> GetRootPrivateKeyAsync(TenantRootCaRecord record, CancellationToken ct)
    {
        if (_rootPrivateKeys.TryGetValue(record.TenantId, out var cached))
            return cached;

        var key = await DecryptRootKeyAsync(record, ct);
        _rootPrivateKeys[record.TenantId] = key;
        return key;
    }

    /// <summary>
    /// Decrypts the root CA private key. The KeyId is stored (UTF-8) in the repurposed
    /// <see cref="TenantRootCaRecord.PrivateKeyNonce"/> column — see the comment in
    /// <see cref="ProvisionTrustAnchorAsync"/>.
    /// </summary>
    private Task<byte[]> DecryptRootKeyAsync(TenantRootCaRecord record, CancellationToken ct)
    {
        var keyId = Encoding.UTF8.GetString(record.PrivateKeyNonce);
        return _secretProtection.DecryptAsync(record.PrivateKeyCiphertext, keyId, ct);
    }

    private static TenantRootCa MapRoot(TenantRootCaRecord record) => new()
    {
        TenantId = record.TenantId,
        CertificateDer = record.CertificateDer,
        SerialNumber = record.SerialNumber,
        SubjectDn = record.SubjectDn,
        Algorithm = record.Algorithm,
        NotBefore = record.NotBefore,
        NotAfter = record.NotAfter,
        CreatedAt = record.CreatedAt,
        KeyProtectionMode = TrustProviderMode.Local,
    };

    private static OrgCertEnrolment MapOrgCert(OrgCertificateRecord record) => new()
    {
        TenantId = record.TenantId,
        OrgWalletAddress = record.OrgWalletAddress,
        CertificateDer = record.CertificateDer,
        SerialNumber = record.SerialNumber,
        SubjectDn = record.SubjectDn,
        SanUri = record.San,
        NotBefore = record.NotBefore,
        NotAfter = record.NotAfter,
        CreatedAt = record.CreatedAt,
        RevokedAt = record.RevokedAt,
        RevocationReason = record.RevocationReason,
    };
}
