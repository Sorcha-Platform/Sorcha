// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Storage;

/// <summary>
/// Feature 181 US4 — persistence for tenant root CAs, org certificates (internal + imported), and CSR
/// audit records (data-model §3, research R8). The write-through <see cref="Trust.InternalCaTrustProvider"/>
/// and <see cref="Trust.OrgCertificateService"/> are the callers; nothing survived restart before this store.
/// </summary>
public interface ICertificateStore
{
    // --- Tenant root CA ---

    /// <summary>The tenant's persisted root CA, or null.</summary>
    Task<TenantRootCaRecord?> GetRootAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Persist a newly provisioned root CA. Idempotent — a no-op if one already exists.</summary>
    Task AddRootAsync(TenantRootCaRecord root, CancellationToken ct = default);

    /// <summary>Atomically increment and return the tenant's next CRL number (RFC 5280 §5.2.3 monotonicity).</summary>
    Task<int> NextCrlNumberAsync(string tenantId, CancellationToken ct = default);

    // --- Org certificates ---

    /// <summary>The Active certificate for a (tenant, org, provenance), or null.</summary>
    Task<OrgCertificateRecord?> GetActiveAsync(
        string tenantId, string orgWalletAddress, OrgCertificateProvenance provenance, CancellationToken ct = default);

    /// <summary>Every certificate for an org (all provenances + statuses), newest first.</summary>
    Task<IReadOnlyList<OrgCertificateRecord>> ListForOrgAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default);

    /// <summary>Fetch a single certificate by id (scoped to the org), or null.</summary>
    Task<OrgCertificateRecord?> GetByIdAsync(
        string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default);

    /// <summary>
    /// Persist a new certificate, superseding any existing Active certificate of the SAME provenance for
    /// the org in the same unit of work (FR-023d).
    /// </summary>
    Task AddAsync(OrgCertificateRecord certificate, CancellationToken ct = default);

    /// <summary>Persist mutations to an existing certificate (status changes: revoke / supersede / key-mismatch).</summary>
    Task UpdateAsync(OrgCertificateRecord certificate, CancellationToken ct = default);

    // --- CSR audit ---

    /// <summary>Persist a generated CSR (FR-018 audit).</summary>
    Task AddCsrAsync(CsrRecord csr, CancellationToken ct = default);

    /// <summary>The most recent CSR generated for an org, or null (key-continuity check at import).</summary>
    Task<CsrRecord?> GetLatestCsrAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default);
}

/// <summary>Postgres-backed <see cref="ICertificateStore"/>.</summary>
public sealed class EfCertificateStore : ICertificateStore
{
    private readonly TenantDbContext _db;

    public EfCertificateStore(TenantDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public Task<TenantRootCaRecord?> GetRootAsync(string tenantId, CancellationToken ct = default)
        => _db.TenantRootCas.FirstOrDefaultAsync(r => r.TenantId == tenantId, ct);

    /// <inheritdoc />
    public async Task AddRootAsync(TenantRootCaRecord root, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var existing = await _db.TenantRootCas.FirstOrDefaultAsync(r => r.TenantId == root.TenantId, ct);
        if (existing is not null)
        {
            return;
        }
        _db.TenantRootCas.Add(root);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> NextCrlNumberAsync(string tenantId, CancellationToken ct = default)
    {
        var root = await _db.TenantRootCas.FirstOrDefaultAsync(r => r.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} has no provisioned root CA.");
        root.CrlNumber += 1;
        await _db.SaveChangesAsync(ct);
        return root.CrlNumber;
    }

    /// <inheritdoc />
    public Task<OrgCertificateRecord?> GetActiveAsync(
        string tenantId, string orgWalletAddress, OrgCertificateProvenance provenance, CancellationToken ct = default)
        => _db.OrgCertificates
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress
                     && c.Provenance == provenance && c.Status == OrgCertificateStatus.Active)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrgCertificateRecord>> ListForOrgAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default)
        => await _db.OrgCertificates
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<OrgCertificateRecord?> GetByIdAsync(
        string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default)
        => _db.OrgCertificates.FirstOrDefaultAsync(
            c => c.Id == certificateId && c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress, ct);

    /// <inheritdoc />
    public async Task AddAsync(OrgCertificateRecord certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var priorActive = await _db.OrgCertificates
            .Where(c => c.TenantId == certificate.TenantId && c.OrgWalletAddress == certificate.OrgWalletAddress
                     && c.Provenance == certificate.Provenance && c.Status == OrgCertificateStatus.Active)
            .ToListAsync(ct);
        foreach (var prior in priorActive)
        {
            prior.Status = OrgCertificateStatus.Superseded;
        }
        _db.OrgCertificates.Add(certificate);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(OrgCertificateRecord certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _db.OrgCertificates.Update(certificate);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task AddCsrAsync(CsrRecord csr, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csr);
        _db.CsrRecords.Add(csr);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<CsrRecord?> GetLatestCsrAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default)
        => _db.CsrRecords
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
}

/// <summary>In-memory <see cref="ICertificateStore"/> (dev/test fallback; warn-tier, not audited).</summary>
public sealed class InMemoryCertificateStore : ICertificateStore
{
    private readonly ConcurrentDictionary<string, TenantRootCaRecord> _roots = new();
    private readonly ConcurrentDictionary<Guid, OrgCertificateRecord> _certs = new();
    private readonly ConcurrentDictionary<Guid, CsrRecord> _csrs = new();
    private readonly object _crlLock = new();

    /// <inheritdoc />
    public Task<TenantRootCaRecord?> GetRootAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult(_roots.TryGetValue(tenantId, out var r) ? r : null);

    /// <inheritdoc />
    public Task AddRootAsync(TenantRootCaRecord root, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        _roots.TryAdd(root.TenantId, root);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> NextCrlNumberAsync(string tenantId, CancellationToken ct = default)
    {
        lock (_crlLock)
        {
            if (!_roots.TryGetValue(tenantId, out var root))
                throw new InvalidOperationException($"Tenant {tenantId} has no provisioned root CA.");
            root.CrlNumber += 1;
            return Task.FromResult(root.CrlNumber);
        }
    }

    /// <inheritdoc />
    public Task<OrgCertificateRecord?> GetActiveAsync(
        string tenantId, string orgWalletAddress, OrgCertificateProvenance provenance, CancellationToken ct = default)
        => Task.FromResult(_certs.Values
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress
                     && c.Provenance == provenance && c.Status == OrgCertificateStatus.Active)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task<IReadOnlyList<OrgCertificateRecord>> ListForOrgAsync(
        string tenantId, string orgWalletAddress, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrgCertificateRecord>>(_certs.Values
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress)
            .OrderByDescending(c => c.CreatedAt)
            .ToList());

    /// <inheritdoc />
    public Task<OrgCertificateRecord?> GetByIdAsync(
        string tenantId, string orgWalletAddress, Guid certificateId, CancellationToken ct = default)
        => Task.FromResult(_certs.TryGetValue(certificateId, out var c)
            && c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress ? c : null);

    /// <inheritdoc />
    public Task AddAsync(OrgCertificateRecord certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        foreach (var prior in _certs.Values
            .Where(c => c.TenantId == certificate.TenantId && c.OrgWalletAddress == certificate.OrgWalletAddress
                     && c.Provenance == certificate.Provenance && c.Status == OrgCertificateStatus.Active))
        {
            prior.Status = OrgCertificateStatus.Superseded;
        }
        _certs[certificate.Id] = certificate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(OrgCertificateRecord certificate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _certs[certificate.Id] = certificate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddCsrAsync(CsrRecord csr, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(csr);
        _csrs[csr.Id] = csr;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CsrRecord?> GetLatestCsrAsync(string tenantId, string orgWalletAddress, CancellationToken ct = default)
        => Task.FromResult(_csrs.Values
            .Where(c => c.TenantId == tenantId && c.OrgWalletAddress == orgWalletAddress)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());
}
