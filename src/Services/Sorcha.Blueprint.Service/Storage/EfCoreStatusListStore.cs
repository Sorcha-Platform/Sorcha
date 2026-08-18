// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// Postgres-backed <see cref="IStatusListStore"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="IDbContextFactory{TContext}"/> because <c>StatusListManager</c> is a singleton
/// and injecting a scoped <c>DbContext</c> into it would be a captive dependency — the same pattern
/// the other singleton EF stores in this service use.
/// </remarks>
public class EfCoreStatusListStore(IDbContextFactory<BlueprintDbContext> contextFactory) : IStatusListStore
{
    /// <inheritdoc />
    public async Task<BitstringStatusList?> GetAsync(string listId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var e = await db.StatusLists.AsNoTracking().FirstOrDefaultAsync(x => x.Id == listId, ct);
        return e is null ? null : ToModel(e);
    }

    /// <inheritdoc />
    public async Task SaveAsync(BitstringStatusList list, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.StatusLists.FirstOrDefaultAsync(x => x.Id == list.Id, ct);
        if (existing is null)
        {
            db.StatusLists.Add(ToEntity(list));
        }
        else
        {
            // Field-by-field, deliberately: ReconciledToDocket is owned by the replay and must NOT
            // be clobbered by a plain save. Every other field is the list's own state.
            existing.IssuerWallet = list.IssuerWallet;
            existing.RegisterId = list.RegisterId;
            existing.Purpose = list.Purpose;
            existing.EncodedList = list.EncodedList;
            existing.Size = list.Size;
            existing.NextAvailableIndex = list.NextAvailableIndex;
            existing.Version = list.Version;
            existing.LastUpdated = list.LastUpdated;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<long?> GetReconciledDocketAsync(string listId, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.StatusLists.AsNoTracking()
            .Where(x => x.Id == listId)
            .Select(x => x.ReconciledToDocket)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task SetReconciledDocketAsync(string listId, long docketNumber, CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var existing = await db.StatusLists.FirstOrDefaultAsync(x => x.Id == listId, ct);
        if (existing is null) return;

        existing.ReconciledToDocket = docketNumber;
        await db.SaveChangesAsync(ct);
    }

    private static BitstringStatusList ToModel(StatusListEntity e) => new()
    {
        Id = e.Id,
        IssuerWallet = e.IssuerWallet,
        RegisterId = e.RegisterId,
        Purpose = e.Purpose,
        EncodedList = e.EncodedList,
        Size = e.Size,
        NextAvailableIndex = e.NextAvailableIndex,
        Version = e.Version,
        LastUpdated = e.LastUpdated
    };

    private static StatusListEntity ToEntity(BitstringStatusList m) => new()
    {
        Id = m.Id,
        IssuerWallet = m.IssuerWallet,
        RegisterId = m.RegisterId,
        Purpose = m.Purpose,
        EncodedList = m.EncodedList,
        Size = m.Size,
        NextAvailableIndex = m.NextAvailableIndex,
        Version = m.Version,
        LastUpdated = m.LastUpdated
    };
}
