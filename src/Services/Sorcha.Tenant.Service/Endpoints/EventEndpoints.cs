// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services.Interfaces;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Activity events REST API endpoints.
/// </summary>
public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/events")
            .WithTags("Events")
            .RequireAuthorization();

        group.MapGet("/", GetEvents)
            .WithName("GetEvents")
            .WithSummary("Get activity events for the authenticated user");

        group.MapGet("/unread-count", GetUnreadCount)
            .WithName("GetUnreadCount")
            .WithSummary("Get unread event count");

        group.MapPost("/mark-read", MarkRead)
            .WithName("MarkEventsRead")
            .WithSummary("Mark events as read");

        group.MapPost("/", CreateEvent)
            .WithName("CreateEvent")
            .WithSummary("Create an activity event (service-to-service)");

        group.MapGet("/admin", GetAdminEvents)
            .WithName("GetAdminEvents")
            .WithSummary("Get events for all users in organisation (admin only)");

        group.MapDelete("/{id:guid}", DeleteEvent)
            .WithName("DeleteEvent")
            .WithSummary("Delete a specific event");
    }

    private static async Task<IResult> GetEvents(
        HttpContext context,
        IEventService eventService,
        int page = 1,
        int pageSize = 50,
        bool unreadOnly = false,
        string? severity = null,
        DateTime? since = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        EventSeverity? severityFilter = null;
        if (!string.IsNullOrEmpty(severity) && Enum.TryParse<EventSeverity>(severity, true, out var parsed))
            severityFilter = parsed;

        var (items, totalCount) = await eventService.GetEventsAsync(
            userId, page, pageSize, unreadOnly, severityFilter, since, ct);

        return TypedResults.Ok(new
        {
            items = items.Select(MapToDto),
            totalCount,
            page,
            pageSize
        });
    }

    private static async Task<IResult> GetUnreadCount(
        HttpContext context,
        IEventService eventService,
        CancellationToken ct = default)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var count = await eventService.GetUnreadCountAsync(userId, ct);
        return TypedResults.Ok(new { count });
    }

    private static async Task<IResult> MarkRead(
        HttpContext context,
        IEventService eventService,
        [FromBody] MarkReadRequest request,
        CancellationToken ct = default)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var eventIds = request.EventIds is { Length: > 0 } ? request.EventIds : null;
        var markedCount = await eventService.MarkReadAsync(userId, eventIds, ct);

        return TypedResults.Ok(new { markedCount });
    }

    private static async Task<IResult> CreateEvent(
        HttpContext context,
        IEventService eventService,
        [FromBody] CreateEventRequest request,
        CancellationToken ct = default)
    {
        var activityEvent = new ActivityEvent
        {
            OrganizationId = request.OrganizationId,
            UserId = request.UserId,
            EventType = request.EventType,
            Severity = Enum.TryParse<EventSeverity>(request.Severity, true, out var sev)
                ? sev : EventSeverity.Info,
            Title = request.Title,
            Message = request.Message,
            SourceService = request.SourceService,
            EntityId = request.EntityId,
            EntityType = request.EntityType
        };

        var created = await eventService.CreateEventAsync(activityEvent, ct);
        return TypedResults.Created($"/api/events/{created.Id}", MapToDto(created));
    }

    private static async Task<IResult> GetAdminEvents(
        HttpContext context,
        IEventService eventService,
        int page = 1,
        int pageSize = 50,
        Guid? userId = null,
        string? severity = null,
        DateTime? since = null,
        CancellationToken ct = default)
    {
        if (!IsAdmin(context))
            return TypedResults.Forbid();

        var orgId = GetOrganizationId(context);
        if (orgId == Guid.Empty) return TypedResults.Unauthorized();

        EventSeverity? severityFilter = null;
        if (!string.IsNullOrEmpty(severity) && Enum.TryParse<EventSeverity>(severity, true, out var parsed))
            severityFilter = parsed;

        var (items, totalCount) = await eventService.GetAdminEventsAsync(
            orgId, page, pageSize, userId, severityFilter, since, ct);

        return TypedResults.Ok(new
        {
            items = items.Select(MapToDto),
            totalCount,
            page,
            pageSize
        });
    }

    private static async Task<IResult> DeleteEvent(
        HttpContext context,
        IEventService eventService,
        Guid id,
        CancellationToken ct = default)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        var deleted = await eventService.DeleteEventAsync(id, userId, ct);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static Guid GetUserId(HttpContext context)
    {
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    private static Guid GetOrganizationId(HttpContext context)
    {
        var orgClaim = context.User.FindFirst("org_id")?.Value;
        return Guid.TryParse(orgClaim, out var id) ? id : Guid.Empty;
    }

    private static bool IsAdmin(HttpContext context)
    {
        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        return role is "Administrator" or "SystemAdmin";
    }

    private static object MapToDto(ActivityEvent e) => new
    {
        e.Id,
        e.EventType,
        severity = e.Severity.ToString(),
        e.Title,
        e.Message,
        e.SourceService,
        e.EntityId,
        e.EntityType,
        e.IsRead,
        e.CreatedAt
    };
}

/// <summary>
/// Request to mark events as read. If EventIds is null or empty, all events are marked read.
/// </summary>
public record MarkReadRequest(Guid[]? EventIds);

/// <summary>
/// Request to create a new activity event.
/// </summary>
public record CreateEventRequest(
    Guid OrganizationId,
    Guid UserId,
    string EventType,
    string Severity,
    string Title,
    string Message,
    string SourceService,
    string? EntityId = null,
    string? EntityType = null);
