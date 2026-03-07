// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;

using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Endpoints;

/// <summary>
/// HTTP endpoints for router health status.
/// </summary>
public static class HealthEndpoints
{
    private static readonly Stopwatch Uptime = Stopwatch.StartNew();

    /// <summary>
    /// Maps the /health endpoint for health checks.
    /// </summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", HandleGetHealth)
            .WithName("GetHealth")
            .WithSummary("Get router health status")
            .WithDescription("Returns overall health, uptime, peer counts, and configuration flags.");

        return app;
    }

    internal static IResult HandleGetHealth(
        RoutingTable table,
        EventBuffer buffer,
        RouterConfiguration config)
    {
        return Results.Ok(new
        {
            status = "Healthy",
            uptime = Uptime.Elapsed,
            totalPeers = table.TotalCount,
            healthyPeers = table.HealthyCount,
            relayEnabled = config.EnableRelay,
            eventBufferSize = buffer.Count
        });
    }
}
