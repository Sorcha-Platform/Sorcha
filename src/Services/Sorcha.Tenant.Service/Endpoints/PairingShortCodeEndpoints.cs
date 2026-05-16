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
/// HTTP surface for F128 pairing short codes — human-typeable 6-digit
/// transport wrapping a standalone enrol-session token.
/// </summary>
public static class PairingShortCodeEndpoints
{
    /// <summary>
    /// Maps <c>POST /api/auth/enrol-session/short-code</c> (mint) and
    /// <c>POST /api/auth/enrol-session/redeem-short-code</c> (redeem).
    /// </summary>
    public static IEndpointRouteBuilder MapPairingShortCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("PairingShortCode");

        group.MapPost("/enrol-session/short-code", MintAsync)
            .WithName("MintPairingShortCode")
            .WithSummary("Mint a 6-digit pairing short code for the signed-in caller.")
            .WithDescription(
                "Returns a 6-digit numeric code with a 5-minute TTL. The code wraps a fresh "
                + "standalone enrol-session token; redeem via "
                + "POST /api/auth/enrol-session/redeem-short-code. Used by the F128 pairing "
                + "takeover sub-affordance and the mobile-web install fallback path.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Accepts<MintPairingShortCodeRequest>("application/json")
            .Produces<MintPairingShortCodeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/enrol-session/redeem-short-code", RedeemAsync)
            .WithName("RedeemPairingShortCode")
            .WithSummary("Redeem a 6-digit pairing short code.")
            .WithDescription(
                "Anonymous — the code is the credential for this single call. Single-use, "
                + "5-attempts-per-code rate-limited. Returns the underlying enrol-session "
                + "redeem result (the same access token shape as POST /api/auth/enrol-session/redeem).")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth)
            .Accepts<RedeemPairingShortCodeRequest>("application/json")
            .Produces<RedeemEnrolSessionResponse>(StatusCodes.Status200OK)
            .Produces<RedeemPairingShortCodeErrorBody>(StatusCodes.Status400BadRequest)
            .Produces<RedeemPairingShortCodeErrorBody>(StatusCodes.Status409Conflict)
            .Produces<RedeemPairingShortCodeErrorBody>(StatusCodes.Status410Gone)
            .Produces<RedeemPairingShortCodeErrorBody>(StatusCodes.Status429TooManyRequests);

        return app;
    }

    internal static async Task<Results<Ok<MintPairingShortCodeResponse>, UnauthorizedHttpResult>> MintAsync(
        ClaimsPrincipal principal,
        IPairingShortCodeService service,
        [FromBody] MintPairingShortCodeRequest? request,
        CancellationToken ct)
    {
        var platformUserId = ResolvePlatformUserId(principal);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var route = request?.Route ?? PairingShortCodeRoute.DesktopHandoff;
        var response = await service.MintAsync(platformUserId.Value, route, ct).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    internal static async Task<IResult> RedeemAsync(
        [FromBody] RedeemPairingShortCodeRequest request,
        IPairingShortCodeService service,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.BadRequest(new RedeemPairingShortCodeErrorBody(
                RedeemPairingShortCodeErrorCode.MalformedCode,
                "Pairing code is required."));
        }

        var result = await service.RedeemAsync(request.Code, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Success);
        }

        return result.Error!.Code switch
        {
            RedeemPairingShortCodeErrorCode.ExpiredCode => Results.Json(result.Error, statusCode: StatusCodes.Status410Gone),
            RedeemPairingShortCodeErrorCode.AlreadyUsedCode => Results.Json(result.Error, statusCode: StatusCodes.Status409Conflict),
            RedeemPairingShortCodeErrorCode.RateLimited => Results.Json(result.Error, statusCode: StatusCodes.Status429TooManyRequests),
            _ => Results.Json(result.Error, statusCode: StatusCodes.Status400BadRequest),
        };
    }

    private static Guid? ResolvePlatformUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(raw, out var parsed) ? parsed : null;
    }
}
