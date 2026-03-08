// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Endpoints;

/// <summary>
/// HTTP endpoints for the real-time debug event stream (snapshot and SSE follow mode).
/// </summary>
public static class EventStreamEndpoints
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Maps the /events endpoint for event stream access.
    /// Without ?follow, returns a JSON snapshot. With ?follow, streams SSE events.
    /// </summary>
    public static WebApplication MapEventStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/events", HandleEventsAsync)
            .WithName("GetEvents")
            .WithSummary("Get router events (snapshot or SSE stream)")
            .WithDescription(
                "Returns buffered router events as JSON. " +
                "Add ?follow query parameter for Server-Sent Events (SSE) live stream.");

        return app;
    }

    internal static async Task<IResult> HandleEventsAsync(
        HttpContext ctx,
        EventBuffer buffer,
        CancellationToken ct)
    {
        var follow = ctx.Request.Query.ContainsKey("follow");
        if (!follow)
        {
            return Results.Ok(buffer.GetSnapshot());
        }

        // SSE follow mode
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["Connection"] = "keep-alive";

        try
        {
            // Send SSE comment to flush headers through reverse proxies (Envoy),
            // then replay buffered events so clients get history on connect.
            await ctx.Response.WriteAsync(": connected\n\n", ct);

            foreach (var past in buffer.GetSnapshot())
            {
                var pastJson = JsonSerializer.Serialize(past, JsonOptions);
                await ctx.Response.WriteAsync($"data: {pastJson}\n\n", ct);
            }

            await ctx.Response.Body.FlushAsync(ct);

            await foreach (var evt in buffer.FollowAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected for SSE
        }

        return Results.Empty;
    }
}
