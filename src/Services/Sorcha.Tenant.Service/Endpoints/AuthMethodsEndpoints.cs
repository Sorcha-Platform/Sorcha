// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Aggregate read of the signed-in user's sign-in methods (Feature 116 US4).
/// Single round-trip that powers the entire Accounts tab UI — password
/// presence, every linked social provider, and every Active/Disabled
/// passkey, with per-row <c>CanRemove</c> flags derived from the same
/// floor helper used by the mutation endpoints.
/// </summary>
public static class AuthMethodsEndpoints
{
    /// <summary>Map the aggregate-read endpoint.</summary>
    public static IEndpointRouteBuilder MapAuthMethodsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/auth-methods", GetMyAuthMethods)
            .WithTags("Auth Methods")
            .WithName("GetMyAuthMethods")
            .WithSummary("List all sign-in methods attached to the signed-in user")
            .WithDescription("Returns email + verification status, password presence, "
                + "every linked social provider, and every Active/Disabled passkey with "
                + "per-row CanRemove flags. Powers the Accounts tab in Settings.")
            .RequireAuthorization()
            .Produces<AuthMethodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<AuthMethodsResponse>, UnauthorizedHttpResult, NotFound>> GetMyAuthMethods(
        IAuthMethodService authMethodService,
        IIdentityRepository identityRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        var aggregate = await authMethodService.GetAggregateAsync(platformUserId.Value, cancellationToken);
        return aggregate is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(aggregate);
    }

    private static async Task<Guid?> ResolvePlatformUserIdAsync(
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                       ?? httpContext.User.FindFirst("pid")?.Value;
        if (Guid.TryParse(pidClaim, out var pid)) return pid;

        var sub = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userIdentityId)) return null;

        var user = await identityRepository.GetUserByIdAsync(userIdentityId, cancellationToken);
        return user?.PlatformUserId;
    }
}
