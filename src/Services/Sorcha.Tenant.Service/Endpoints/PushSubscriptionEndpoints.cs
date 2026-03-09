// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for managing push notification subscriptions.
/// Users can subscribe/unsubscribe their browser for push notifications.
/// </summary>
public static class PushSubscriptionEndpoints
{
    /// <summary>
    /// Maps push subscription endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPushSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push-subscriptions")
            .WithTags("PushSubscriptions")
            .RequireAuthorization();

        group.MapPost("/", Subscribe)
            .WithName("SubscribePush")
            .WithSummary("Register a push notification subscription")
            .WithDescription("Registers or updates a Web Push API subscription for the authenticated user. "
                + "Requires the push endpoint URL and VAPID keys (p256dh and auth).");

        group.MapDelete("/", Unsubscribe)
            .WithName("UnsubscribePush")
            .WithSummary("Remove a push notification subscription")
            .WithDescription("Removes an existing push notification subscription by endpoint URL. "
                + "The user will no longer receive push notifications on that browser/device.");

        group.MapGet("/status", GetStatus)
            .WithName("GetPushStatus")
            .WithSummary("Check if user has an active push subscription")
            .WithDescription("Returns whether the authenticated user has at least one active push notification subscription.");

        return app;
    }

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static async Task<IResult> Subscribe(
        PushSubscriptionRequest request,
        ClaimsPrincipal user,
        TenantDbContext db)
    {
        var userId = GetUserId(user);
        if (userId is null) return TypedResults.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Endpoint) || request.Keys is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["endpoint"] = ["Endpoint and keys are required"]
            });

        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId.Value && s.Endpoint == request.Endpoint);

        if (existing is not null)
        {
            existing.P256dhKey = request.Keys.P256dh;
            existing.AuthKey = request.Keys.Auth;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Endpoint = request.Endpoint,
                P256dhKey = request.Keys.P256dh,
                AuthKey = request.Keys.Auth,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return TypedResults.Ok(new { subscribed = true });
    }

    private static async Task<IResult> Unsubscribe(
        string endpoint,
        ClaimsPrincipal user,
        TenantDbContext db)
    {
        var userId = GetUserId(user);
        if (userId is null) return TypedResults.Unauthorized();

        var subscription = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId.Value && s.Endpoint == endpoint);

        if (subscription is not null)
        {
            db.PushSubscriptions.Remove(subscription);
            await db.SaveChangesAsync();
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetStatus(
        ClaimsPrincipal user,
        TenantDbContext db)
    {
        var userId = GetUserId(user);
        if (userId is null) return TypedResults.Unauthorized();

        var hasSubscription = await db.PushSubscriptions
            .AnyAsync(s => s.UserId == userId.Value);

        return TypedResults.Ok(new { hasActiveSubscription = hasSubscription });
    }
}

/// <summary>
/// Request to create a push notification subscription.
/// </summary>
public class PushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public PushSubscriptionKeys? Keys { get; set; }
}

/// <summary>
/// Web Push API subscription keys.
/// </summary>
public class PushSubscriptionKeys
{
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}
