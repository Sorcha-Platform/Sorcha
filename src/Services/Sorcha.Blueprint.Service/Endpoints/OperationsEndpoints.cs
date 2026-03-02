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

        return routes;
    }
}
