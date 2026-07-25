// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Public inbox endpoints for the authenticated platform user. Phase 5 (US3) of Feature 118.
/// </summary>
/// <remarks>
/// All endpoints are scoped to the caller's <c>platform_user_id</c> claim so a
/// caller cannot read or mutate another user's entries. <c>404 Not Found</c> is
/// returned indistinguishably for entries that don't exist or aren't owned by
/// the caller — preventing a cross-user enumeration oracle.
/// </remarks>
public static class MeInboxEndpoints
{
    /// <summary>Maps the <c>/api/me/inbox/*</c> endpoints.</summary>
    public static IEndpointRouteBuilder MapMeInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/inbox")
            .RequireAuthorization()
            .WithTags("Inbox");

        group.MapGet("", ListAsync)
            .WithName("ListMyInbox")
            .WithSummary("List the authenticated user's inbox entries.")
            .WithDescription("Paginated, sorted newest first. Excludes dismissed entries unless includeDismissed=true. Use actionableOnly=true to restrict results to Actionable entries (Category==Action or Severity>=ActionRequired).");

        group.MapGet("unread-count", GetUnreadCountAsync)
            .WithName("GetMyInboxUnreadCount")
            .WithSummary("Return the user's unread inbox count.")
            .WithDescription("Returns the user's unread needs-attention count: Category == Action, or Severity at "
                + "Warning or above (issue #1267 — a Workflow/Warning decision notice previously could not "
                + "badge the bell). Info entries are excluded so the badge is not a generic unread count. "
                + "Realtime updates are pushed via TenantHub InboxUnreadCountUpdated.");

        group.MapGet("{id:guid}", GetByIdAsync)
            .WithName("GetMyInboxEntry")
            .WithSummary("Fetch a single inbox entry by id.")
            .WithDescription("Returns 404 indistinguishably if the entry does not exist or is not owned by the caller.");

        group.MapPost("{id:guid}/read", MarkReadAsync)
            .WithName("MarkMyInboxEntryRead")
            .WithSummary("Mark an entry as read. Idempotent.");

        group.MapPost("{id:guid}/dismiss", DismissAsync)
            .WithName("DismissMyInboxEntry")
            .WithSummary("Dismiss an entry. Idempotent.");

        group.MapPost("mark-all-read", MarkAllReadAsync)
            .WithName("MarkAllMyInboxEntriesRead")
            .WithSummary("Mark every unread entry for the authenticated user read.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IInboxService service,
        int page = 1,
        int pageSize = 20,
        InboxCategory? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
        bool actionableOnly = false,
        CancellationToken ct = default)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();

        var result = await service.GetPageAsync(
            userId, page, pageSize, category, unreadOnly, includeDismissed, actionableOnly, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetUnreadCountAsync(
        HttpContext context,
        IInboxService service,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();
        var count = await service.GetUnreadCountAsync(userId, ct);
        return Results.Ok(new { unread = count });
    }

    private static async Task<IResult> GetByIdAsync(
        HttpContext context,
        IInboxService service,
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();
        var entry = await service.GetByIdAsync(userId, id, ct);
        return entry is null ? Results.NotFound() : Results.Ok(entry);
    }

    private static async Task<IResult> MarkReadAsync(
        HttpContext context,
        IInboxService service,
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();
        var ok = await service.MarkReadAsync(userId, id, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DismissAsync(
        HttpContext context,
        IInboxService service,
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();
        var ok = await service.DismissAsync(userId, id, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> MarkAllReadAsync(
        HttpContext context,
        IInboxService service,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return Results.Unauthorized();
        var marked = await service.MarkAllReadAsync(userId, ct);
        return Results.Ok(new { marked });
    }

    private static Guid GetUserId(HttpContext context)
    {
        var raw = context.User.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
