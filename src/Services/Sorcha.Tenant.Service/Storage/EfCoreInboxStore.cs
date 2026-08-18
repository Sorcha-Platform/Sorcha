// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Storage;

/// <summary>
/// Default <see cref="IInboxStore"/> implementation backed by
/// <see cref="TenantDbContext"/>. Postgres in production; InMemory EF provider
/// in tests via the same code path.
/// </summary>
public sealed class EfCoreInboxStore : IInboxStore
{
    private readonly TenantDbContext _db;

    /// <summary>Initialises a new <see cref="EfCoreInboxStore"/>.</summary>
    public EfCoreInboxStore(TenantDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public Task<bool> PlatformUserExistsAsync(Guid platformUserId, CancellationToken ct = default) =>
        _db.PlatformUsers.AsNoTracking().AnyAsync(u => u.Id == platformUserId, ct);

    public async Task<InboxAddResult> AddOrFindAsync(InboxEntry candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing = await FindBySourceEventAsync(candidate.PlatformUserId, candidate.SourceEventId, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new InboxAddResult(existing, IsIdempotent: true);
        }

        _db.InboxEntries.Add(candidate);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent write may have won the unique-index race on
            // (PlatformUserId, SourceEventId). Re-read.
            var raced = await FindBySourceEventAsync(candidate.PlatformUserId, candidate.SourceEventId, ct)
                .ConfigureAwait(false);
            if (raced is not null)
            {
                return new InboxAddResult(raced, IsIdempotent: true);
            }
            throw;
        }

        return new InboxAddResult(candidate, IsIdempotent: false);
    }

    /// <inheritdoc />
    public async Task<InboxPageResult> GetPageAsync(
        Guid platformUserId,
        int page,
        int pageSize,
        InboxCategory? category,
        bool unreadOnly,
        bool includeDismissed,
        bool actionableOnly = false,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.InboxEntries
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId);

        if (!includeDismissed)
        {
            query = query.Where(e => e.DismissedAt == null);
        }
        if (unreadOnly)
        {
            query = query.Where(e => e.ReadAt == null);
        }
        if (category.HasValue)
        {
            query = query.Where(e => e.Category == category.Value);
        }
        if (actionableOnly)
        {
            query = query.Where(InboxClassification.ActionablePredicate);
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var entries = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new InboxPageResult(entries, total);
    }

    /// <inheritdoc />
    public Task<InboxEntry?> GetByIdAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        return _db.InboxEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct);
    }

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(Guid platformUserId, bool actionableOnly = false, CancellationToken ct = default)
    {
        var query = _db.InboxEntries
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId && e.ReadAt == null && e.DismissedAt == null);

        if (actionableOnly)
        {
            // Issue #1267: the BADGE counts "needs attention" (Action or Warning+), not the narrower
            // "actionable" (Action or ActionRequired+). A rejected identity application is
            // Workflow/Warning, so under the old predicate it could never badge the bell — the
            // citizen saw an empty bell and concluded their application had vanished. The list
            // filter above keeps the narrower ActionablePredicate: that is a user-chosen filter with
            // different intent from an attention indicator.
            query = query.Where(InboxClassification.NeedsAttentionPredicate);
        }

        return query.CountAsync(ct);
    }

    /// <inheritdoc />
    public async Task<InboxMarkReadResult> MarkReadAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.InboxEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return new InboxMarkReadResult(Found: false, StateChanged: false);
        }

        if (entry.ReadAt is not null)
        {
            return new InboxMarkReadResult(Found: true, StateChanged: false);
        }

        entry.ReadAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InboxMarkReadResult(Found: true, StateChanged: true);
    }

    /// <inheritdoc />
    public async Task<InboxDismissResult> DismissAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.InboxEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return new InboxDismissResult(Found: false, StateChanged: false, WasUnread: false);
        }

        if (entry.DismissedAt is not null)
        {
            return new InboxDismissResult(Found: true, StateChanged: false, WasUnread: false);
        }

        var wasUnread = entry.ReadAt is null;
        entry.DismissedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InboxDismissResult(Found: true, StateChanged: true, WasUnread: wasUnread);
    }

    /// <inheritdoc />
    public async Task<int> MarkAllReadAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Use a tracked-entity update path so InMemory provider tests pass; on Postgres
        // the same path generates a single UPDATE batch. ExecuteUpdateAsync would be more
        // efficient for very large unread inboxes — kept as a follow-up once the InMemory
        // factory grows a bulk-op helper.
        var unread = await _db.InboxEntries
            .Where(e => e.PlatformUserId == platformUserId && e.ReadAt == null && e.DismissedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var entry in unread)
        {
            entry.ReadAt = now;
        }
        if (unread.Count > 0)
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return unread.Count;
    }

    private Task<InboxEntry?> FindBySourceEventAsync(Guid platformUserId, Guid sourceEventId, CancellationToken ct)
    {
        return _db.InboxEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.PlatformUserId == platformUserId && e.SourceEventId == sourceEventId,
                ct);
    }
}
