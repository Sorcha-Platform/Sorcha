// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Hubs;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IInboxService"/> implementation. Postgres source of truth
/// (via <see cref="TenantDbContext"/>); SignalR fan-out to TenantHub on every
/// state transition.
/// </summary>
/// <remarks>
/// <para>
/// Phase 5 v1: unread-count is computed via <c>COUNT(*)</c>. The Redis sorted-set
/// index from research R-002 is a phase-5 follow-up — at this user volume the
/// COUNT query is sub-millisecond, and adding the Redis index without first
/// observing real load would be premature.
/// </para>
/// <para>
/// SignalR emits use the untyped <c>SendAsync</c> path because
/// <see cref="ITenantHubClient"/> is empty until Phase 5 also lands the hub
/// methods themselves. The methods below send under the same names that
/// the typed-client interface declares, so the contract stays consistent.
/// </para>
/// </remarks>
public sealed class InboxService : IInboxService
{
    private readonly TenantDbContext _db;
    private readonly IHubContext<TenantHub> _hub;
    private readonly ILogger<InboxService> _logger;

    /// <summary>Initialises a new <see cref="InboxService"/>.</summary>
    public InboxService(TenantDbContext db, IHubContext<TenantHub> hub, ILogger<InboxService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InboxWriteResult> WriteAsync(InboxWriteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Idempotency check on (PlatformUserId, SourceEventId).
        var existing = await _db.InboxEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.PlatformUserId == request.PlatformUserId && e.SourceEventId == request.SourceEventId,
                ct).ConfigureAwait(false);
        if (existing is not null)
        {
            _logger.LogDebug(
                "Inbox idempotent write — entry already exists. PlatformUserId={PlatformUserId} SourceEventId={SourceEventId} EntryId={EntryId}",
                request.PlatformUserId, request.SourceEventId, existing.Id);
            return new InboxWriteResult(existing, IsIdempotent: true);
        }

        var entry = new InboxEntry
        {
            Id = Guid.NewGuid(),
            PlatformUserId = request.PlatformUserId,
            Category = request.Category,
            Severity = request.Severity,
            CorrelationKey = request.CorrelationKey,
            DetailHref = request.DetailHref,
            SourceEventId = request.SourceEventId,
            OccurredAt = request.OccurredAt,
            Title = request.Title,
            Summary = request.Summary,
            IconKey = request.IconKey,
            ChannelHints = request.ChannelHints ?? DefaultChannelHintsFor(request.Category),
            WriterServiceId = request.WriterServiceId,
        };

        _db.InboxEntries.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another concurrent write may have won the unique-index race. Re-read.
            var raced = await _db.InboxEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.PlatformUserId == request.PlatformUserId && e.SourceEventId == request.SourceEventId,
                    ct).ConfigureAwait(false);
            if (raced is not null)
            {
                return new InboxWriteResult(raced, IsIdempotent: true);
            }
            throw;
        }

        _logger.LogInformation(
            "Inbox entry written. PlatformUserId={PlatformUserId} EntryId={EntryId} Category={Category} CorrelationKey={CorrelationKey}",
            entry.PlatformUserId, entry.Id, entry.Category, entry.CorrelationKey);

        await EmitInboxEntryAddedAsync(entry, ct).ConfigureAwait(false);
        await EmitUnreadCountAsync(entry.PlatformUserId, ct).ConfigureAwait(false);

        return new InboxWriteResult(entry, IsIdempotent: false);
    }

    /// <inheritdoc />
    public async Task<InboxPage> GetPageAsync(
        Guid platformUserId,
        int page = 1,
        int pageSize = 20,
        InboxCategory? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
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

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var entries = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        return new InboxPage(entries, page, pageSize, total);
    }

    /// <inheritdoc />
    public async Task<InboxEntry?> GetByIdAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        return await _db.InboxEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(Guid platformUserId, CancellationToken ct = default)
    {
        return await _db.InboxEntries
            .AsNoTracking()
            .Where(e => e.PlatformUserId == platformUserId && e.ReadAt == null && e.DismissedAt == null)
            .CountAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> MarkReadAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.InboxEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        if (entry.ReadAt is null)
        {
            entry.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DismissAsync(Guid platformUserId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await _db.InboxEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.PlatformUserId == platformUserId, ct)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        var wasUnread = entry.ReadAt is null && entry.DismissedAt is null;
        if (entry.DismissedAt is null)
        {
            entry.DismissedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (wasUnread)
            {
                await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
            }
        }
        return true;
    }

    /// <inheritdoc />
    public async Task<int> MarkAllReadAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // Use a tracked-entity update path so InMemory provider tests pass; on Postgres
        // the same path generates a single UPDATE. ExecuteUpdateAsync would be more efficient
        // for very large unread inboxes — Phase 5 follow-up swaps it in once we add a bulk
        // op to the InMemoryDbContextFactory test helper.
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
            await EmitUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
        }
        return unread.Count;
    }

    private static ChannelHints DefaultChannelHintsFor(InboxCategory category) => category switch
    {
        InboxCategory.Action       => ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email,
        InboxCategory.Credential   => ChannelHints.Inbox | ChannelHints.Push,
        InboxCategory.Membership   => ChannelHints.Inbox | ChannelHints.Email,
        InboxCategory.Security     => ChannelHints.Inbox | ChannelHints.Push | ChannelHints.Email,
        InboxCategory.System       => ChannelHints.Inbox,
        InboxCategory.Workflow     => ChannelHints.Inbox,
        _                          => ChannelHints.Inbox,
    };

    private async Task EmitInboxEntryAddedAsync(InboxEntry entry, CancellationToken ct)
    {
        try
        {
            // Thin signal: entry id, occurredAt, traceId. No content on the wire.
            await _hub.Clients.Group(TenantHubGroups.User(entry.PlatformUserId))
                .SendAsync(
                    "InboxEntryAdded",
                    entry.Id.ToString("N"),
                    entry.OccurredAt,
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "",
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit InboxEntryAdded for {EntryId} to {PlatformUserId}",
                entry.Id, entry.PlatformUserId);
        }
    }

    private async Task EmitUnreadCountAsync(Guid platformUserId, CancellationToken ct)
    {
        try
        {
            var count = await GetUnreadCountAsync(platformUserId, ct).ConfigureAwait(false);
            await _hub.Clients.Group(TenantHubGroups.User(platformUserId))
                .SendAsync(
                    "InboxUnreadCountUpdated",
                    count,
                    DateTimeOffset.UtcNow,
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "",
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to emit InboxUnreadCountUpdated for {PlatformUserId}",
                platformUserId);
        }
    }
}
