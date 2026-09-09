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
            var bytes = await ReadSubmissionBodyAsync(ctx.Request, ct);

            if (bytes.Length == 0)
                return Results.BadRequest(new { error = "submission body is required" });

            var (targets, accepted) =
                await distributionService.ForwardSubmissionAsync(registerId, bytes, ct);

            return Results.Ok(new
            {
                targetPeerCount = targets,
                acceptedCount = accepted
            });
        })
        .WithName("DistributeTransactionSubmission")
        .WithTags("Peer — Feature 108")
        .WithSummary("Fan a signed transaction submission out to source peers for the register")
        .RequireAuthorization(AuthorizationPolicies.CanWriteDockets)
        .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Reads the submission body in full and returns what was actually read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> gated on <c>Content-Length</c>. A chunked request carries none, so
    /// the previous <c>ContentLength is null or 0</c> guard refused it as bodyless — with an error
    /// naming the caller's own body — while the body sat right there and this read handles it fine.
    /// Emptiness is a property of the bytes, not of a header a legitimate sender need not set
    /// (#1624).
    /// </para>
    /// <para>
    /// This is Feature 108's cross-node fan-out, so the failure would have been a silently lost
    /// submission with the investigation pointed at the client. That is not hypothetical: the same
    /// pattern on blueprint publish (#1618) broke the MCP tool and the CLI while the browser UI kept
    /// working — browsers set Content-Length, Refit and HttpClient send chunked — and produced a
    /// confident misdiagnosis of "authorisation".
    /// </para>
    /// <para>
    /// Extracted from the handler so the property is reachable from a unit test: transfer framing
    /// is otherwise only observable over a real HTTP connection, which is exactly why nothing
    /// caught #1618.
    /// </para>
    /// </remarks>
    internal static async Task<byte[]> ReadSubmissionBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var ms = new System.IO.MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
