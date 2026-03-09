// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services.Interfaces;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages activity events for the activity log system.
/// </summary>
public class EventService : IEventService
{
    private readonly TenantDbContext _db;
    private readonly ILogger<EventService> _logger;
    private const int RetentionDays = 90;

    public EventService(TenantDbContext db, ILogger<EventService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetEventsAsync(
        Guid userId, int page, int pageSize, bool unreadOnly = false,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.ActivityEvents
            .Where(e => e.UserId == userId)
            .AsQueryable();

        if (unreadOnly)
            query = query.Where(e => !e.IsRead);
        if (severity.HasValue)
            query = query.Where(e => e.Severity == severity.Value);
        if (since.HasValue)
            query = query.Where(e => e.CreatedAt >= since.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.ActivityEvents
            .CountAsync(e => e.UserId == userId && !e.IsRead, ct);
    }

    public async Task<int> MarkReadAsync(Guid userId, Guid[]? eventIds = null, CancellationToken ct = default)
    {
        if (eventIds is { Length: > 0 })
        {
            return await _db.ActivityEvents
                .Where(e => e.UserId == userId && eventIds.Contains(e.Id) && !e.IsRead)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsRead, true), ct);
        }

        // Mark all as read
        return await _db.ActivityEvents
            .Where(e => e.UserId == userId && !e.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsRead, true), ct);
    }

    public async Task<ActivityEvent> CreateEventAsync(ActivityEvent activityEvent, CancellationToken ct = default)
    {
        activityEvent.CreatedAt = DateTime.UtcNow;
        activityEvent.ExpiresAt = activityEvent.CreatedAt.AddDays(RetentionDays);

        _db.ActivityEvents.Add(activityEvent);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Activity event created: {EventType} for user {UserId} in org {OrgId}",
            activityEvent.EventType, activityEvent.UserId, activityEvent.OrganizationId);

        return activityEvent;
    }

    public async Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetAdminEventsAsync(
        Guid organizationId, int page, int pageSize, Guid? userId = null,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.ActivityEvents
            .Where(e => e.OrganizationId == organizationId)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);
        if (severity.HasValue)
            query = query.Where(e => e.Severity == severity.Value);
        if (since.HasValue)
            query = query.Where(e => e.CreatedAt >= since.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<bool> DeleteEventAsync(Guid eventId, Guid userId, CancellationToken ct = default)
    {
        var deleted = await _db.ActivityEvents
            .Where(e => e.Id == eventId && e.UserId == userId)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}
