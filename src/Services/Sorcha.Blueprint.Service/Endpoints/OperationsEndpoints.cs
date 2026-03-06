// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Endpoints for polling encryption operation status.
/// Provides fallback for clients without SignalR connectivity.
/// </summary>
public static class OperationsEndpoints
{
    /// <summary>
    /// Map the operations polling endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/operations")
            .WithTags("Operations")
            .RequireAuthorization();

        group.MapGet("/{operationId}", async (string operationId, IEncryptionOperationStore store, HttpContext httpContext) =>
        {
            var operation = await store.GetByIdAsync(operationId);
            if (operation is null) return Results.NotFound();

            // Verify the requesting user owns this operation.
            // The wallet address in the JWT (if present) must match the submitting wallet,
            // or the user's sub claim must be present (service-to-service calls are trusted).
            var walletClaim = httpContext.User.FindFirst("wallet_address")?.Value;
            if (walletClaim != null && !string.Equals(walletClaim, operation.SubmittingWalletAddress, StringComparison.Ordinal))
            {
                return Results.Forbid();
            }

            return Results.Ok(operation);
        })
        .WithName("GetEncryptionOperation")
        .WithSummary("Get encryption operation status")
        .WithDescription("Returns current status of an async encryption operation. Use for polling fallback when SignalR is unavailable.");

        group.MapGet("/", async (
            string wallet,
            int? page,
            int? pageSize,
            IEncryptionOperationStore store,
            IEventService eventService,
            HttpContext httpContext) =>
        {
            // Authorize: wallet_address claim must match if present.
            var walletClaim = httpContext.User.FindFirst("wallet_address")?.Value;
            if (walletClaim != null && !string.Equals(walletClaim, wallet, StringComparison.Ordinal))
            {
                return Results.Forbid();
            }

            var effectivePage = Math.Max(1, page ?? 1);
            var effectivePageSize = Math.Clamp(pageSize ?? 20, 1, 50);

            // Get the userId from JWT for querying activity events.
            var subClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.User.FindFirst("sub")?.Value;

            var historyItems = new List<object>();

            // 1. Get active in-memory operation for this wallet (at most one).
            var activeOp = await store.GetByWalletAddressAsync(wallet);
            if (activeOp != null)
            {
                historyItems.Add(new
                {
                    operationId = activeOp.OperationId,
                    status = activeOp.Status.ToString().ToLowerInvariant(),
                    blueprintId = activeOp.BlueprintId,
                    actionTitle = activeOp.StepName,
                    instanceId = activeOp.InstanceId,
                    walletAddress = activeOp.SubmittingWalletAddress,
                    recipientCount = activeOp.TotalRecipients,
                    transactionHash = activeOp.TransactionHash,
                    errorMessage = activeOp.Error,
                    createdAt = activeOp.CreatedAt,
                    completedAt = activeOp.CompletedAt
                });
            }

            // 2. Get completed/failed operations from activity events.
            if (Guid.TryParse(subClaim, out var userId))
            {
                // Fetch a generous page of events; we filter by type afterwards.
                var (events, _) = await eventService.GetEventsAsync(
                    userId, 1, 200, since: null, ct: httpContext.RequestAborted);

                var encryptionEvents = events
                    .Where(e => e.EntityType == "EncryptionOperation")
                    .OrderByDescending(e => e.CreatedAt);

                foreach (var evt in encryptionEvents)
                {
                    // Skip if the active operation already covers this ID.
                    if (activeOp != null && evt.EntityId == activeOp.OperationId)
                        continue;

                    var status = evt.EventType switch
                    {
                        var t when t.Contains("complete", StringComparison.OrdinalIgnoreCase) => "complete",
                        var t when t.Contains("failed", StringComparison.OrdinalIgnoreCase) => "failed",
                        _ => "unknown"
                    };

                    historyItems.Add(new
                    {
                        operationId = evt.EntityId,
                        status,
                        blueprintId = (string?)null,
                        actionTitle = evt.Title,
                        instanceId = (string?)null,
                        walletAddress = wallet,
                        recipientCount = 0,
                        transactionHash = (string?)null,
                        errorMessage = status == "failed" ? evt.Message : null,
                        createdAt = (DateTimeOffset)evt.CreatedAt,
                        completedAt = (DateTimeOffset?)evt.CreatedAt
                    });
                }
            }

            var totalCount = historyItems.Count;
            var pagedItems = historyItems
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToList();

            return Results.Ok(new
            {
                items = pagedItems,
                page = effectivePage,
                pageSize = effectivePageSize,
                totalCount,
                hasMore = effectivePage * effectivePageSize < totalCount
            });
        })
        .WithName("ListEncryptionOperations")
        .WithSummary("List encryption operations for a wallet")
        .WithDescription("Returns a paginated list of encryption operations (active and historical) for the specified wallet address.");

        return routes;
    }
}
