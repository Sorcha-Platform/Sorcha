// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Re-authentication challenge endpoints (Feature 116). The two-step
/// initiate/verify flow gates every sensitive auth-method mutation in the
/// Tenant Service: removing a sign-in method, changing a password, disabling 2FA.
/// </summary>
public static class AuthChallengeEndpoints
{
    /// <summary>Maps the challenge endpoints to the application.</summary>
    public static IEndpointRouteBuilder MapAuthChallengeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/challenge")
            .WithTags("Auth Challenge");

        group.MapPost("/initiate", InitiateChallenge)
            .WithName("InitiateAuthChallenge")
            .WithSummary("Begin a re-authentication challenge")
            .WithDescription("Selects a proof method per the ladder (TOTP → Password → "
                + "Passkey → re-OAuth) and prepares the appropriate dialog input. "
                + "Returns 400 when the user has no enrolled method (only reachable in "
                + "the bootstrap edge case).")
            .RequireAuthorization()
            .Produces<ChallengeInitiateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/verify", VerifyChallenge)
            .WithName("VerifyAuthChallenge")
            .WithSummary("Submit challenge proof and receive a one-shot token")
            .WithDescription("Verifies the user's proof for an in-flight challenge. On "
                + "success, returns an opaque single-use token (5-minute lifetime) that "
                + "the caller presents in the X-Auth-Challenge header on the subsequent "
                + "mutation call.")
            .RequireAuthorization()
            .Produces<ChallengeVerifyResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<ChallengeInitiateResponse>, BadRequest<string>, UnauthorizedHttpResult>> InitiateChallenge(
        ChallengeInitiateRequest request,
        IAuthChallengeService challengeService,
        IIdentityRepository identityRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ctx = await ResolveContextAsync(httpContext, identityRepository, cancellationToken);
        if (ctx is null) return TypedResults.Unauthorized();

        var prep = await challengeService.InitiateAsync(
            ctx.Value, request.ScopedOperation, request.PreferredMethod, request.TargetMethodKind, cancellationToken);

        if (!prep.IsAvailable)
        {
            return TypedResults.BadRequest(
                "No re-authentication method is enrolled for this account.");
        }

        return TypedResults.Ok(new ChallengeInitiateResponse(prep.Method, prep.Payload));
    }

    private static async Task<Results<Ok<ChallengeVerifyResponse>, UnauthorizedHttpResult, ProblemHttpResult>> VerifyChallenge(
        ChallengeVerifyRequest request,
        IAuthChallengeService challengeService,
        IIdentityRepository identityRepository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ctx = await ResolveContextAsync(httpContext, identityRepository, cancellationToken);
        if (ctx is null) return TypedResults.Unauthorized();

        var result = await challengeService.VerifyAsync(
            ctx.Value, request.Method, request.ScopedOperation, request.Proof, request.TargetMethodKind, cancellationToken);

        // Floor-rule violation (Feature 150): the proof was the wrong tier for the operation.
        // Distinct from a rejected/invalid proof — a 403 with a machine-readable reason code.
        if (result.Outcome == ChallengeVerificationOutcome.ProofTierInsufficient)
        {
            return TypedResults.Problem(
                title: "Proof tier insufficient",
                detail: "proof_tier_insufficient",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!result.Succeeded || result.Token is null || result.ExpiresAt is null)
        {
            return TypedResults.Unauthorized();
        }

        var ttl = (int)(result.ExpiresAt.Value - DateTimeOffset.UtcNow).TotalSeconds;
        return TypedResults.Ok(new ChallengeVerifyResponse(result.Token, Math.Max(0, ttl)));
    }

    /// <summary>
    /// Resolves the (PlatformUserId, UserIdentityId) pair from the bearer claims.
    /// The sub claim carries UserIdentity.Id; PlatformUserId either rides on a
    /// custom claim or is looked up via the identity repository.
    /// </summary>
    private static async Task<ChallengeContext?> ResolveContextAsync(
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var sub = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userIdentityId)) return null;

        // Prefer the custom claim — avoids a DB hit on every challenge call.
        var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                       ?? httpContext.User.FindFirst("pid")?.Value;
        if (Guid.TryParse(pidClaim, out var platformUserId))
        {
            return new ChallengeContext(platformUserId, userIdentityId);
        }

        // Fallback: look up via the identity repository.
        var user = await identityRepository.GetUserByIdAsync(userIdentityId, cancellationToken);
        if (user is null || user.PlatformUserId == Guid.Empty) return null;

        return new ChallengeContext(user.PlatformUserId, userIdentityId);
    }
}
