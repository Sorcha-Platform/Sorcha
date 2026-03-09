// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services.Interfaces;

/// <summary>
/// Service for managing activity events (activity log).
/// </summary>
public interface IEventService
{
    /// <summary>
    /// Gets paginated activity events for a specific user with optional filters.
    /// </summary>
    Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetEventsAsync(
        Guid userId, int page, int pageSize, bool unreadOnly = false,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the count of unread events for a specific user.
    /// </summary>
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Marks events as read. If eventIds is null or empty, marks all events for the user as read.
    /// </summary>
    Task<int> MarkReadAsync(Guid userId, Guid[]? eventIds = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a new activity event.
    /// </summary>
    Task<ActivityEvent> CreateEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>
    /// Gets paginated activity events for an organization (admin view) with optional filters.
    /// </summary>
    Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetAdminEventsAsync(
        Guid organizationId, int page, int pageSize, Guid? userId = null,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific event owned by the user.
    /// </summary>
    Task<bool> DeleteEventAsync(Guid eventId, Guid userId, CancellationToken ct = default);
}
