// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

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
            int pageSize = 20) =>
        {
            var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;
            if (string.IsNullOrEmpty(walletAddress))
            {
                return Results.Ok(new { items = Array.Empty<object>(), totalCount = 0, page, pageSize });
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
            + "Supports pagination. Urgency and blueprint filtering will be added in a future iteration.")
        .WithOpenApi(operation =>
        {
            OpenApiExamples.SetResponseExample(operation, "200", """
                {
                  "items": [
                    {
                      "instanceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "actionId": "review-application",
                      "blueprintTitle": "Construction Permit Application",
                      "participantRole": "BuildingInspector",
                      "status": "Pending",
                      "createdAt": "2026-03-15T10:30:00Z",
                      "dueDate": "2026-03-22T10:30:00Z"
                    }
                  ],
                  "totalCount": 1,
                  "page": 1,
                  "pageSize": 20
                }
                """);
            return operation;
        });

        group.MapGet("/pending/count", async (
            HttpContext httpContext,
            IInstanceStore instanceStore) =>
        {
            var walletAddress = httpContext.User.FindFirst("wallet_address")?.Value;
            if (string.IsNullOrEmpty(walletAddress))
            {
                return Results.Ok(new { count = 0, urgentCount = 0 });
            }

            var count = await instanceStore.GetPendingActionCountByWalletAsync(walletAddress);

            // TODO: urgentCount requires urgency-aware query — tracked for next iteration
            return Results.Ok(new { count, urgentCount = 0 });
        })
        .WithName("GetPendingActionCount")
        .WithSummary("Get pending action count for badge display")
        .WithDescription("Returns the count of pending actions for the authenticated user's wallet address. "
            + "urgentCount is currently always 0 — urgency-aware counting will be added in a future iteration.");

        return routes;
    }
}
