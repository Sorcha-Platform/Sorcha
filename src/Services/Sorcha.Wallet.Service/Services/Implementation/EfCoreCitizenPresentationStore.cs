// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// PostgreSQL-backed <see cref="ICitizenPresentationStore"/> over
/// <see cref="WalletDbContext"/> (Feature 114, US5 PR3). The durable, authoritative
/// home for the citizen's cross-device presentation history.
/// </summary>
/// <remarks>
/// Avoids <c>ExecuteDeleteAsync</c>/<c>ExecuteUpdateAsync</c> so the same code path
/// runs under the EF Core InMemory provider in store unit tests (the
/// <c>TestCitizenWalletDbContext</c> pattern). Holds disclosed claim names only —
/// never values — and no register correlation (FR-002 / FR-010).
/// </remarks>
public sealed class EfCoreCitizenPresentationStore : ICitizenPresentationStore
{
    private readonly WalletDbContext _db;

    /// <summary>Initialise a new instance.</summary>
    public EfCoreCitizenPresentationStore(WalletDbContext db)
        => _db = db ?? throw new ArgumentNullException(nameof(db));

    /// <inheritdoc />
    public async Task UpsertAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CitizenPresentationStoreMetrics.RecordOp("upsert");

        var existing = await _db.CitizenPresentationRecords
            .FirstOrDefaultAsync(e => e.PlatformUserId == platformUserId && e.EntryId == entry.Id, ct)
            .ConfigureAwait(false);

        // Idempotent on (platformUserId, entryId): a re-report preserves the
        // original ReportedAt and the immutable content. Nothing to update.
        if (existing is not null) return;

        _db.CitizenPresentationRecords.Add(new CitizenPresentationRecord
        {
            PlatformUserId = platformUserId,
            EntryId = entry.Id,
            CredentialId = entry.CredentialId,
            VerifierLabel = entry.VerifierLabel,
            VerifierDid = entry.VerifierDid,
            DisclosedClaims = [.. entry.DisclosedClaims],
            PresentedAt = entry.PresentedAt,
            Outcome = (int)entry.Outcome,
            ReportedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PresentationLogEntry>> ListAsync(Guid platformUserId, CancellationToken ct = default)
    {
        CitizenPresentationStoreMetrics.RecordOp("list");

        var rows = await _db.CitizenPresentationRecords
            .Where(e => e.PlatformUserId == platformUserId)
            .OrderByDescending(e => e.PresentedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(ToWire).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        CitizenPresentationStoreMetrics.RecordOp("delete");

        var existing = await _db.CitizenPresentationRecords
            .FirstOrDefaultAsync(e => e.PlatformUserId == platformUserId && e.EntryId == entryId, ct)
            .ConfigureAwait(false);

        if (existing is null) return false;

        _db.CitizenPresentationRecords.Remove(existing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static PresentationLogEntry ToWire(CitizenPresentationRecord r) => new()
    {
        Id = r.EntryId,
        CredentialId = r.CredentialId,
        VerifierDid = r.VerifierDid,
        VerifierLabel = r.VerifierLabel,
        DisclosedClaims = r.DisclosedClaims,
        PresentedAt = r.PresentedAt,
        Outcome = (PresentationLogOutcome)r.Outcome
        // RegisterId / ActionTxId intentionally null — vestigial, not persisted (FR-010).
    };
}
