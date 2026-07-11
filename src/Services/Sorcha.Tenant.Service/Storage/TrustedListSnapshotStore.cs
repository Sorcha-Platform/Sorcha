// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Storage;

/// <summary>
/// Feature 181 US3 — persistence for imported ETSI TS 119 612 trusted-list snapshots (data-model §2).
/// The newest <see cref="TrustedListSnapshotStatus.Active"/> snapshot per <c>trustListId</c> is
/// authoritative; importing a newer sequence supersedes the prior Active version (kept for audit).
/// </summary>
public interface ITrustedListSnapshotStore
{
    /// <summary>
    /// Persist a new snapshot, marking any existing Active snapshot for the same
    /// <see cref="TrustedListSnapshot.TrustListId"/> as <see cref="TrustedListSnapshotStatus.Superseded"/>
    /// in the same unit of work.
    /// </summary>
    Task AddAsync(TrustedListSnapshot snapshot, CancellationToken ct = default);

    /// <summary>The current Active snapshot (with anchors) for a list identity, or null.</summary>
    Task<TrustedListSnapshot?> GetActiveAsync(string trustListId, CancellationToken ct = default);

    /// <summary>Current sequence number of the Active snapshot for a list identity, or null when none.</summary>
    Task<long?> GetActiveSequenceAsync(string trustListId, CancellationToken ct = default);

    /// <summary>All Active snapshots (with anchors), newest import first.</summary>
    Task<IReadOnlyList<TrustedListSnapshot>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>Delete every version of a list identity. Returns whether anything was removed (idempotent).</summary>
    Task<bool> DeleteAsync(string trustListId, CancellationToken ct = default);
}

/// <summary>Postgres-backed <see cref="ITrustedListSnapshotStore"/>.</summary>
public sealed class EfTrustedListSnapshotStore : ITrustedListSnapshotStore
{
    private readonly TenantDbContext _db;

    public EfTrustedListSnapshotStore(TenantDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task AddAsync(TrustedListSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var priorActive = await _db.TrustedListSnapshots
            .Where(s => s.TrustListId == snapshot.TrustListId && s.Status == TrustedListSnapshotStatus.Active)
            .ToListAsync(ct);
        foreach (var prior in priorActive)
        {
            prior.Status = TrustedListSnapshotStatus.Superseded;
        }

        _db.TrustedListSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<TrustedListSnapshot?> GetActiveAsync(string trustListId, CancellationToken ct = default)
        => _db.TrustedListSnapshots
            .Include(s => s.Anchors)
            .Where(s => s.TrustListId == trustListId && s.Status == TrustedListSnapshotStatus.Active)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<long?> GetActiveSequenceAsync(string trustListId, CancellationToken ct = default)
    {
        var seq = await _db.TrustedListSnapshots
            .Where(s => s.TrustListId == trustListId && s.Status == TrustedListSnapshotStatus.Active)
            .Select(s => (long?)s.SequenceNumber)
            .OrderByDescending(s => s)
            .FirstOrDefaultAsync(ct);
        return seq;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrustedListSnapshot>> ListActiveAsync(CancellationToken ct = default)
        => await _db.TrustedListSnapshots
            .Include(s => s.Anchors)
            .Where(s => s.Status == TrustedListSnapshotStatus.Active)
            .OrderByDescending(s => s.ImportedAt)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string trustListId, CancellationToken ct = default)
    {
        var versions = await _db.TrustedListSnapshots
            .Where(s => s.TrustListId == trustListId)
            .ToListAsync(ct);
        if (versions.Count == 0)
        {
            return false;
        }

        _db.TrustedListSnapshots.RemoveRange(versions);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>In-memory <see cref="ITrustedListSnapshotStore"/> (dev/test fallback; warn-tier, not audited).</summary>
public sealed class InMemoryTrustedListSnapshotStore : ITrustedListSnapshotStore
{
    private readonly ConcurrentDictionary<Guid, TrustedListSnapshot> _snapshots = new();

    /// <inheritdoc />
    public Task AddAsync(TrustedListSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var prior in _snapshots.Values
            .Where(s => s.TrustListId == snapshot.TrustListId && s.Status == TrustedListSnapshotStatus.Active))
        {
            prior.Status = TrustedListSnapshotStatus.Superseded;
        }
        _snapshots[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TrustedListSnapshot?> GetActiveAsync(string trustListId, CancellationToken ct = default)
        => Task.FromResult(_snapshots.Values
            .Where(s => s.TrustListId == trustListId && s.Status == TrustedListSnapshotStatus.Active)
            .OrderByDescending(s => s.SequenceNumber)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task<long?> GetActiveSequenceAsync(string trustListId, CancellationToken ct = default)
        => Task.FromResult(_snapshots.Values
            .Where(s => s.TrustListId == trustListId && s.Status == TrustedListSnapshotStatus.Active)
            .Select(s => (long?)s.SequenceNumber)
            .OrderByDescending(s => s)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task<IReadOnlyList<TrustedListSnapshot>> ListActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrustedListSnapshot>>(_snapshots.Values
            .Where(s => s.Status == TrustedListSnapshotStatus.Active)
            .OrderByDescending(s => s.ImportedAt)
            .ToList());

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string trustListId, CancellationToken ct = default)
    {
        var toRemove = _snapshots.Values.Where(s => s.TrustListId == trustListId).Select(s => s.Id).ToList();
        foreach (var id in toRemove)
        {
            _snapshots.TryRemove(id, out _);
        }
        return Task.FromResult(toRemove.Count > 0);
    }
}
