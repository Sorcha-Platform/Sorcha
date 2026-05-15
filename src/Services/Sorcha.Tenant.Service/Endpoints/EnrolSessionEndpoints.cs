// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Enrolment-session HTTP surface for Feature 126.
/// </summary>
public static class EnrolSessionEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/auth/enrol-session</c> and
    /// <c>POST /api/auth/enrol-session/redeem</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapEnrolSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("EnrolSession");

        group.MapPost("/enrol-session", MintAsync)
            .WithName("MintEnrolSession")
            .WithSummary("Mint a one-time enrolment session token for the signed-in caller.")
            .WithDescription(
                "Returns a JWT scoped to 'enrol', a QR URL embedding the token, and the absolute "
                + "expiry timestamp. The caller must be authenticated. Used by council pages to "
                + "render the enrolment QR/link/paste affordance on the wallet-pairing surface.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Produces<MintEnrolSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/enrol-session/redeem", RedeemAsync)
            .WithName("RedeemEnrolSession")
            .WithSummary("Redeem a one-time enrolment session token.")
            .WithDescription(
                "Anonymous (the token is the credential). Single-use: subsequent attempts to "
                + "redeem the same token return 409. Returns the bound user's display name and "
                + "email so the wallet PWA can surface a confirmation dialog before any device-"
                + "pairing happens.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Produces<RedeemEnrolSessionResponse>(StatusCodes.Status200OK)
            .Produces<RedeemEnrolSessionErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<RedeemEnrolSessionErrorBody>(StatusCodes.Status409Conflict)
            .Produces<RedeemEnrolSessionErrorBody>(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<Results<Ok<MintEnrolSessionResponse>, UnauthorizedHttpResult>> MintAsync(
        ClaimsPrincipal principal,
        IEnrolSessionService service,
        CancellationToken ct)
    {
        var platformUserId = ResolvePlatformUserId(principal);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var response = await service.MintAsync(platformUserId.Value, ct).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> RedeemAsync(
        [FromBody] RedeemEnrolSessionRequest request,
        IEnrolSessionService service,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SessionToken))
        {
            return TypedResults.BadRequest(new RedeemEnrolSessionErrorBody(
                RedeemEnrolSessionErrorCode.MalformedToken,
                "Session token is required."));
        }

        var result = await service.RedeemAsync(request.SessionToken, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Success);
        }

        return result.Error!.Code switch
        {
            RedeemEnrolSessionErrorCode.Expired => Results.Json(result.Error, statusCode: StatusCodes.Status410Gone),
            RedeemEnrolSessionErrorCode.AlreadyUsed => Results.Json(result.Error, statusCode: StatusCodes.Status409Conflict),
            _ => Results.Json(result.Error, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static Guid? ResolvePlatformUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
