// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Citizen wallet pending-application notice endpoints (Feature 124). Mounted
/// under <c>/api/v1/wallet/pending-applications</c> alongside the existing
/// <see cref="CitizenWalletEndpoints"/>. Carries only a human-readable label;
/// no credential content.
/// </summary>
public static class PendingApplicationEndpoints
{
    /// <summary>Maps the pending-application notice endpoints.</summary>
    public static IEndpointRouteBuilder MapPendingApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallet/pending-applications")
            .WithTags("Citizen Wallet")
            // Feature 147 / review F124: consumer-tier only, matching every sibling citizen surface.
            // Plain .RequireAuthorization() let a platform token read/set a citizen's notice.
            .RequireAuthorization(Microsoft.Extensions.Hosting.AuthorizationPolicies.RequireConsumerAudience)
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapGet("", GetPendingApplication)
            .WithName("GetPendingApplication")
            .WithSummary("Read the citizen's current pending-application notice")
            .WithDescription(
                "Returns the active notice or null. The wallet calls this on every Home " +
                "render to decide whether to show the waiting state. Notice is scoped to " +
                "the calling PlatformUser via JWT.")
            .Produces<PendingApplicationEnvelope>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("", SetPendingApplication)
            .WithName("SetPendingApplication")
            .WithSummary("Set or replace the citizen's pending-application notice")
            .WithDescription(
                "Idempotent. If a prior notice exists, the new label replaces it and the " +
                "TTL resets. Notice expires after 24 hours unless explicitly cleared first.")
            .Produces<PendingApplicationEnvelope>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("", ClearPendingApplication)
            .WithName("ClearPendingApplication")
            .WithSummary("Clear the citizen's pending-application notice")
            .WithDescription(
                "Idempotent. Returns 204 whether or not a notice was present. The wallet's " +
                "waiting state clears within one second on next Home render.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetPendingApplication(
        HttpContext context,
        IPendingApplicationStore store,
        CancellationToken ct)
    {
        var platformUserId = ResolvePlatformUserId(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        var notice = await store.GetAsync(platformUserId.Value, ct);
        return Results.Ok(new PendingApplicationEnvelope { Notice = notice });
    }

    private static async Task<IResult> SetPendingApplication(
        [FromBody] SetPendingApplicationRequest request,
        HttpContext context,
        IValidator<SetPendingApplicationRequest> validator,
        IPendingApplicationStore store,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var platformUserId = ResolvePlatformUserId(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        var notice = await store.SetAsync(platformUserId.Value, request.Label.Trim(), ct);
        logger.LogInformation(
            "Pending-application notice set platformUser={PlatformUserId} label={Label}",
            platformUserId, notice.Label);
        return Results.Ok(new PendingApplicationEnvelope { Notice = notice });
    }

    private static async Task<IResult> ClearPendingApplication(
        HttpContext context,
        IPendingApplicationStore store,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var platformUserId = ResolvePlatformUserId(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        await store.ClearAsync(platformUserId.Value, ct);
        logger.LogInformation(
            "Pending-application notice cleared platformUser={PlatformUserId}",
            platformUserId);
        return Results.NoContent();
    }

    private static Guid? ResolvePlatformUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("platform_user_id") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var pid) ? pid : null;
    }
}
