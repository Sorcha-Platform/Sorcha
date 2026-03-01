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

        group.MapGet("/{operationId}", async (string operationId, IEncryptionOperationStore store) =>
        {
            var operation = await store.GetByIdAsync(operationId);
            return operation is null ? Results.NotFound() : Results.Ok(operation);
        })
        .WithName("GetEncryptionOperation")
        .WithSummary("Get encryption operation status")
        .WithDescription("Returns current status of an async encryption operation. Use for polling fallback when SignalR is unavailable.");

        return routes;
    }
}
