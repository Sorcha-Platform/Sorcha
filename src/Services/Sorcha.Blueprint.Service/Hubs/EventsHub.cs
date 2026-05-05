// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// SignalR hub for real-time activity event notifications.
/// </summary>
/// <remarks>
/// Connection URL: /hubs/events
/// Authentication: JWT token via query parameter ?access_token={jwt}
///
/// Client Methods (called by server):
/// - EventReceived(ActivityEventDto): New event pushed in real-time
/// - UnreadCountUpdated(int count): Updated unread count
///
/// Server Methods (called by clients):
/// - Subscribe(): Join user's personal event group
/// - SubscribeOrg(): Join organisation event group (admin only)
/// - Unsubscribe(): Leave personal event group
/// - UnsubscribeOrg(): Leave organisation event group
/// </remarks>
[Authorize]
public class EventsHub : Hub<IEventsHubClient>
{
    private readonly ILogger<EventsHub> _logger;

    public EventsHub(ILogger<EventsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected to EventsHub. ConnectionId: {ConnectionId}, User: {User}",
            Context.ConnectionId,
            Context.UserIdentifier ?? "anonymous");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(exception,
                "Client disconnected from EventsHub with error. ConnectionId: {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to personal events. Auto-joins user:{userId} group.
    /// </summary>
    public async Task Subscribe()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId))
            throw new HubException("Unauthorized: missing identity claims");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        _logger.LogInformation("User {UserId} subscribed to personal events", userId);
    }

    /// <summary>
    /// Subscribe to organisation-wide events (admin only).
    /// </summary>
    public async Task SubscribeOrg()
    {
        if (!IsAdmin())
            throw new HubException("Unauthorized: admin role required");

        var orgId = GetOrganizationId();
        if (string.IsNullOrEmpty(orgId))
            throw new HubException("Unauthorized: missing organisation claim");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{orgId}");
        _logger.LogInformation("Admin subscribed to org {OrgId} events", orgId);
    }

    /// <summary>
    /// Leave personal event group.
    /// </summary>
    public async Task Unsubscribe()
    {
        var userId = GetUserId();
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");
    }

    /// <summary>
    /// Leave organisation event group.
    /// </summary>
    public async Task UnsubscribeOrg()
    {
        var orgId = GetOrganizationId();
        if (!string.IsNullOrEmpty(orgId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"org:{orgId}");
    }

    private string? GetUserId() =>
        Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? Context.User?.FindFirst("sub")?.Value;

    private string? GetOrganizationId() =>
        Context.User?.FindFirst("org_id")?.Value;

    private bool IsAdmin()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        return role is "Administrator" or "SystemAdmin";
    }
}
