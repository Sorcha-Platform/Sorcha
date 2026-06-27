// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Pre-session step-up challenge and link-confirm endpoints for the step-up social account
/// linking flow (Feature 168). All three endpoints are unauthenticated — the link-pending
/// token acts as the principal for initiate/verify, and both the link-pending token plus the
/// challenge token are the credentials at link-confirm.
/// </summary>
public static class SocialLinkStepUpEndpoints
{
    /// <summary>Maps the social link step-up endpoints to the application.</summary>
    public static IEndpointRouteBuilder MapSocialLinkStepUpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/social/link")
            .WithTags("Social Link Step-Up");

        group.MapPost("/challenge/initiate", InitiateLinkChallenge)
            .WithName("InitiateSocialLinkChallenge")
            .WithSummary("Begin a step-up challenge for social account linking")
            .WithDescription("Accepts a valid link-pending token and begins the step-up challenge "
                + "against the target account scoped to ScopedOperation.LinkSocial. Delegates to "
                + "the existing IAuthChallengeService — the link-pending token acts as the principal "
                + "rather than a bearer. No authentication header required (Feature 168).")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<ChallengeInitiateResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/challenge/verify", VerifyLinkChallenge)
            .WithName("VerifySocialLinkChallenge")
            .WithSummary("Submit challenge proof for social account linking step-up")
            .WithDescription("Verifies the user's proof for the LinkSocial challenge. On success, "
                + "returns an opaque single-use token (5-minute lifetime) to present in the "
                + "X-Auth-Challenge header at link-confirm. No authentication header required (Feature 168).")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<ChallengeVerifyResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/confirm", ConfirmSocialLink)
            .WithName("ConfirmSocialLink")
            .WithSummary("Redeem link-pending token and step-up proof to link a social identity")
            .WithDescription("Verifies the link-pending token (signature + expiry) and consumes "
                + "a valid LinkSocial step-up challenge proof. Asserts the challenge is bound to "
                + "the same account as the token's target. On success, links the social identity "
                + "via ISocialLinkService.LinkAsync and issues the same session as a normal social "
                + "sign-in. Non-leaky status codes: 401 for invalid/expired/missing credentials, "
                + "403 for account mismatch or wrong operation, 409 for link-time collision (Feature 168).")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<TokenResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>
    /// POST /api/auth/social/link/challenge/initiate — begin a LinkSocial step-up challenge
    /// against the target account identified by the link-pending token.
    /// </summary>
    private static async Task<IResult> InitiateLinkChallenge(
        SocialLinkChallengeInitiateRequest request,
        ILinkPendingTokenService linkPendingTokenService,
        IAuthChallengeService challengeService,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!linkPendingTokenService.TryVerify(request.LinkPendingToken, out var linkToken, out var tokenError))
        {
            logger.LogWarning(
                "Social link challenge initiate: link-pending token invalid, error={Error}", tokenError);
            return TypedResults.Unauthorized();
        }

        var ctx = await ResolveChallengeContextAsync(linkToken.TargetAccountId, identityRepository, ct);
        if (ctx is null)
        {
            logger.LogWarning(
                "Social link challenge initiate: target account {TargetAccountId} not found or has no identity",
                linkToken.TargetAccountId);
            return TypedResults.Unauthorized();
        }

        var prep = await challengeService.InitiateAsync(
            ctx.Value,
            ScopedOperation.LinkSocial,
            request.PreferredMethod,
            targetMethodKind: null,
            ct);

        if (!prep.IsAvailable)
        {
            return TypedResults.Problem(
                "No re-authentication method is enrolled for this account.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return TypedResults.Ok(new ChallengeInitiateResponse(prep.Method, prep.Payload));
    }

    /// <summary>
    /// POST /api/auth/social/link/challenge/verify — verify the step-up proof for the
    /// LinkSocial challenge and return a single-use challenge token.
    /// </summary>
    private static async Task<IResult> VerifyLinkChallenge(
        SocialLinkChallengeVerifyRequest request,
        ILinkPendingTokenService linkPendingTokenService,
        IAuthChallengeService challengeService,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (!linkPendingTokenService.TryVerify(request.LinkPendingToken, out var linkToken, out var tokenError))
        {
            logger.LogWarning(
                "Social link challenge verify: link-pending token invalid, error={Error}", tokenError);
            return TypedResults.Unauthorized();
        }

        var ctx = await ResolveChallengeContextAsync(linkToken.TargetAccountId, identityRepository, ct);
        if (ctx is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await challengeService.VerifyAsync(
            ctx.Value,
            request.Method,
            ScopedOperation.LinkSocial,
            request.Proof,
            targetMethodKind: null,
            ct);

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
    /// POST /api/auth/social/link/confirm — redeem link-pending token + LinkSocial challenge proof,
    /// link the social identity, and issue a session.
    /// Steps are fail-closed per contracts/link-confirm.md (FR-005, FR-006, FR-007, FR-008).
    /// </summary>
    private static async Task<IResult> ConfirmSocialLink(
        LinkConfirmRequest request,
        HttpContext httpContext,
        ILinkPendingTokenService linkPendingTokenService,
        IAuthChallengeRepository challengeRepository,
        ISocialLinkService socialLinkService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        TenantDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Step 1: verify link-pending token.
        if (!linkPendingTokenService.TryVerify(request.LinkPendingToken, out var linkToken, out var tokenError))
        {
            logger.LogWarning("Social link confirm: link-pending token rejected, error={Error}", tokenError);
            SocialLoginMetrics.RecordLinkConfirm(string.Empty, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 2: require X-Auth-Challenge header.
        var rawChallenge = httpContext.Request.Headers[Filters.RequireAuthChallengeAttribute.HeaderName].ToString();
        if (string.IsNullOrEmpty(rawChallenge))
        {
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 3: look up the challenge token by hash.
        var tokenHash = ComputeSha256Hex(rawChallenge);
        var challengeToken = await challengeRepository.FindByHashAsync(tokenHash, ct);
        if (challengeToken is null)
        {
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 3b: wrong operation — the challenge must be scoped to LinkSocial.
        if (challengeToken.ScopedOperation != ScopedOperation.LinkSocial)
        {
            logger.LogWarning(
                "Social link confirm: challenge scoped to {IssuedOp}, expected LinkSocial",
                challengeToken.ScopedOperation);
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 3c: not expired.
        if (challengeToken.ExpiresAt < DateTimeOffset.UtcNow)
        {
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 4: assert challenge is bound to the link-pending token's target account.
        if (challengeToken.PlatformUserId != linkToken.TargetAccountId)
        {
            logger.LogWarning(
                "Social link confirm: challenge bound to {BoundAccount} but token targets {TargetAccount}",
                challengeToken.PlatformUserId, linkToken.TargetAccountId);
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Forbid();
        }

        // Atomic consume — single use (replay → 401).
        var consumed = await challengeRepository.TryConsumeAsync(challengeToken.Id, DateTimeOffset.UtcNow, ct);
        if (!consumed)
        {
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "rejected");
            return TypedResults.Unauthorized();
        }

        // Step 5: link the social identity.
        var linkOutcome = await socialLinkService.LinkAsync(
            linkToken.TargetAccountId,
            linkToken.Provider,
            linkToken.Subject,
            linkToken.SocialEmail,
            linkToken.DisplayName,
            ct);

        if (linkOutcome is SocialLinkOutcome.AlreadyLinkedToDifferentUser or SocialLinkOutcome.EmailCollision)
        {
            logger.LogWarning(
                "Social link confirm collision for {Provider}/{Subject}: outcome={Outcome}",
                linkToken.Provider, linkToken.Subject, linkOutcome);
            SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "conflict");
            return TypedResults.Problem(
                "This social identity is already linked to a different account.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // Step 6: issue session — same path as the social callback.
        var publicOrgId = WellKnownIds.PublicOrgId;
        var userIdentity = await db.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == linkToken.TargetAccountId
                                   && u.OrganizationId == publicOrgId, ct);

        if (userIdentity is null)
        {
            logger.LogError(
                "Social link confirm: no UserIdentity in public org for PlatformUser {PlatformUserId}",
                linkToken.TargetAccountId);
            return TypedResults.Problem(
                "Account configuration error.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var publicOrg = await organizationRepository.GetByIdAsync(publicOrgId, ct);
        if (publicOrg is null)
        {
            return TypedResults.Problem(
                "Public organisation not found.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        userIdentity.LastLoginAt = DateTimeOffset.UtcNow;
        await identityRepository.UpdateUserAsync(userIdentity, ct);

        var tokenResponse = await tokenService.GenerateUserTokenAsync(
            userIdentity, publicOrg, linkToken.TargetAccountId, Tier.Platform, ct);

        logger.LogInformation(
            "Social link confirm succeeded for PlatformUser {PlatformUserId} provider={Provider}",
            linkToken.TargetAccountId, linkToken.Provider);

        SocialLoginMetrics.RecordLinkConfirm(linkToken.Provider, "success");
        return TypedResults.Ok(tokenResponse);
    }

    /// <summary>
    /// Resolves the <see cref="ChallengeContext"/> for the target account by looking up its
    /// primary UserIdentity in the public org. The UserIdentityId is required so the challenge
    /// service can key TOTP/passkey state correctly.
    /// </summary>
    private static async Task<ChallengeContext?> ResolveChallengeContextAsync(
        Guid platformUserId,
        IIdentityRepository identityRepository,
        CancellationToken ct)
    {
        var identity = await identityRepository.GetUserByPlatformUserAndOrgAsync(
            platformUserId, WellKnownIds.PublicOrgId, ct);
        if (identity is null) return null;

        return new ChallengeContext(platformUserId, identity.Id);
    }

    private static string ComputeSha256Hex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
