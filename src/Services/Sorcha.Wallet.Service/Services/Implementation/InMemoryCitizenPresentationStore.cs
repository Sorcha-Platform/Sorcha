// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// In-memory <see cref="ICitizenPresentationStore"/> — the development/test fallback
/// when no PostgreSQL connection string is configured (Feature 114, US5 PR3).
/// Registered as a singleton so the data survives across request scopes; convenience
/// data only, so it warns (not gates) at startup per the storage registration log.
/// </summary>
/// <remarks>
/// Same semantics as <see cref="EfCoreCitizenPresentationStore"/>: idempotent upsert
/// keyed on <c>(platformUserId, entryId)</c> preserving the original
/// <c>ReportedAt</c>, newest-first list, and citizen-scoped delete.
/// </remarks>
public sealed class InMemoryCitizenPresentationStore : ICitizenPresentationStore
{
    private sealed record Stored(PresentationLogEntry Entry, DateTimeOffset ReportedAt);

    // Keyed by (platformUserId, entryId).
    private readonly ConcurrentDictionary<(Guid, Guid), Stored> _entries = new();

    /// <inheritdoc />
    public Task UpsertAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CitizenPresentationStoreMetrics.RecordOp("upsert");

        // Idempotent: first writer wins; a re-report preserves the original entry
        // (and therefore its ReportedAt).
        _entries.TryAdd((platformUserId, entry.Id), new Stored(entry, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PresentationLogEntry>> ListAsync(Guid platformUserId, CancellationToken ct = default)
    {
        CitizenPresentationStoreMetrics.RecordOp("list");

        var rows = _entries
            .Where(kvp => kvp.Key.Item1 == platformUserId)
            .Select(kvp => kvp.Value.Entry)
            .OrderByDescending(e => e.PresentedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<PresentationLogEntry>>(rows);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        CitizenPresentationStoreMetrics.RecordOp("delete");
        return Task.FromResult(_entries.TryRemove((platformUserId, entryId), out _));
    }
}
