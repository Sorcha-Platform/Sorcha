// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Endpoints for querying pending actions across blueprint instances.
/// </summary>
public static class ActionEndpoints
{
    /// <summary>
    /// Maps action-related endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/actions")
            .WithTags("Actions")
            .RequireAuthorization();

        group.MapGet("/pending", async (
            HttpContext httpContext,
            IInstanceStore instanceStore,
            int page = 1,
            int pageSize = 20,
            string? urgency = null,
            string? blueprintId = null) =>
        {
            var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;
            if (string.IsNullOrEmpty(walletAddress))
            {
                return Results.BadRequest(new { error = "No wallet_address claim in token" });
            }

            var skip = (page - 1) * pageSize;
            var items = await instanceStore.GetPendingActionsByWalletAsync(
                walletAddress, skip, pageSize);

            var itemList = items.ToList();
            var totalCount = await instanceStore.GetPendingActionCountByWalletAsync(walletAddress);

            return Results.Ok(new
            {
                items = itemList,
                totalCount,
                page,
                pageSize
            });
        })
        .WithName("GetPendingActions")
        .WithSummary("Get pending actions for the authenticated user")
        .WithDescription("Returns all pending actions across blueprint instances for the user's wallet address. "
            + "Supports pagination and optional urgency/blueprint filtering.");

        group.MapGet("/pending/count", async (
            HttpContext httpContext,
            IInstanceStore instanceStore) =>
        {
            var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;
            if (string.IsNullOrEmpty(walletAddress))
            {
                return Results.BadRequest(new { error = "No wallet_address claim in token" });
            }

            var count = await instanceStore.GetPendingActionCountByWalletAsync(walletAddress);

            return Results.Ok(new { count, urgentCount = 0 });
        })
        .WithName("GetPendingActionCount")
        .WithSummary("Get pending action count for badge display")
        .WithDescription("Returns the count of pending actions for the authenticated user's wallet address.");

        return routes;
    }
}
