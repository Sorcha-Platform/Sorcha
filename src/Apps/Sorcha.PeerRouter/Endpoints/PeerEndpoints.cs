// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Endpoints;

/// <summary>
/// HTTP endpoints for querying the peer routing table.
/// </summary>
public static class PeerEndpoints
{
    /// <summary>
    /// Maps the /peers endpoint for peer table queries.
    /// </summary>
    public static WebApplication MapPeerEndpoints(this WebApplication app)
    {
        app.MapGet("/peers", HandleGetPeers)
            .WithName("GetPeers")
            .WithSummary("Get all known peers")
            .WithDescription("Returns the full peer routing table with health status and metadata.");

        return app;
    }

    internal static IResult HandleGetPeers(RoutingTable table)
    {
        var peers = table.GetAllPeers();
        return Results.Ok(new
        {
            totalPeers = table.TotalCount,
            healthyPeers = table.HealthyCount,
            peers
        });
    }
}
