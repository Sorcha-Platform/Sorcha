// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Sorcha.Peer.Service.Distribution;

namespace Sorcha.Peer.Service.Endpoints;

/// <summary>
/// Feature 108. Internal endpoint Blueprint.Service calls to fan a signed transaction
/// submission out to source peers. Reuses the existing outbound gRPC channel pool so NAT
/// traversal is not a concern.
/// </summary>
public static class DistributeEndpoints
{
    public static IEndpointRouteBuilder MapDistributeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/peer/distribute/{registerId}", async (
            HttpContext ctx,
            string registerId,
            TransactionDistributionService distributionService,
            CancellationToken ct) =>
        {
            if (ctx.Request.ContentLength is null or 0)
                return Results.BadRequest(new { error = "submission body is required" });

            using var ms = new System.IO.MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var (targets, accepted, locallyOwned) =
                await distributionService.ForwardSubmissionAsync(registerId, bytes, ct);

            return Results.Ok(new
            {
                targetPeerCount = targets,
                acceptedCount = accepted,
                locallyOwned = locallyOwned
            });
        })
        .WithName("DistributeTransactionSubmission")
        .WithTags("Peer — Feature 108")
        .WithSummary("Fan a signed transaction submission out to source peers for the register")
        .RequireAuthorization(AuthorizationPolicies.CanWriteDockets)
        .ExcludeFromDescription();

        return app;
    }
}
