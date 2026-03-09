// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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

            var historyItems = new List<object>();

            // Get active in-memory operation for this wallet (at most one).
            // Historical data is available from Tenant Service /api/events endpoint.
            var activeOp = await store.GetByWalletAddressAsync(wallet);
            if (activeOp != null)
            {
                historyItems.Add(new
                {
                    operationId = activeOp.OperationId,
                    status = activeOp.Status.ToString().ToLowerInvariant(),
                    blueprintId = activeOp.BlueprintId,
                    actionTitle = $"Action {activeOp.ActionId}",
                    instanceId = activeOp.InstanceId,
                    walletAddress = activeOp.SubmittingWalletAddress,
                    recipientCount = activeOp.TotalRecipients,
                    transactionHash = activeOp.TransactionHash,
                    errorMessage = activeOp.Error,
                    createdAt = activeOp.CreatedAt,
                    completedAt = activeOp.CompletedAt
                });
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
        .WithSummary("List active encryption operations for a wallet")
        .WithDescription("Returns active in-memory encryption operations for the specified wallet. For historical data, query Tenant Service /api/events.");

        return routes;
    }
}
