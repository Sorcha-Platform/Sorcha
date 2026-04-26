// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Public endpoint for serving citizen device delegation status lists
/// (Feature 114). Verifiers fetch these to refresh their cached revocation
/// status; the path is anonymous (no auth) because verifiers are external
/// parties without a Sorcha account.
/// </summary>
public static class CitizenStatusListEndpoints
{
    /// <summary>Maps the citizen-device status list endpoint.</summary>
    public static IEndpointRouteBuilder MapCitizenStatusListEndpoints(this IEndpointRouteBuilder app)
    {
        // Wide rate-limit policy — content is cacheable, verifier-side traffic is bounded
        // by the 24h refresh interval, and no auth means we want generous burst capacity
        // before the centralised limiter kicks in.
        var group = app.MapGroup("/api/v1/wallet/status")
            .WithTags("Citizen Wallet — Status Lists")
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/{orgId:guid}/citizen-devices/{listId:int}.statuslist+jwt", GetSignedList)
            .WithName("GetCitizenDeviceStatusList")
            .WithSummary("Fetch a signed citizen-device status list (public)")
            .WithDescription(
                "Returns the IETF Token Status List 2024 JWT for the given org + list. " +
                "Public — no auth — so external verifiers can refresh their cached " +
                "revocation status without Sorcha credentials. Cache-Control reflects the " +
                "list's exp, so well-behaved clients honour the 24h freshness window.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/statuslist+jwt")
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetSignedList(
        [FromRoute] Guid orgId,
        [FromRoute] int listId,
        [FromServices] ICitizenStatusListPublisher publisher,
        CancellationToken ct)
    {
        var jwt = await publisher.GetSignedListAsync(orgId, listId, ct);
        if (jwt is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Status list not found",
                Detail = $"No citizen-device status list exists for org {orgId} listId {listId}.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Convey staleness window to verifiers / edge caches; the publisher writes a fresh
        // 24h exp on every regeneration so a 6h browser-side TTL keeps lists effectively
        // current while limiting fetch volume.
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = "public, max-age=21600"
        };

        return Results.Text(
            content: jwt,
            contentType: "application/statuslist+jwt",
            statusCode: StatusCodes.Status200OK);
    }
}
