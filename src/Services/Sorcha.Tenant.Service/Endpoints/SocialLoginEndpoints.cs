// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Filters;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Social login endpoints for platform-level authentication via OAuth2/OIDC providers.
/// Handles initiation, callback completion, and provider linking flows.
/// </summary>
public static class SocialLoginEndpoints
{
    /// <summary>
    /// The single canonical OAuth callback path for the environment. Both
    /// the initiate and link flows pass <c>{baseUrl}{CallbackPath}</c> as
    /// the <c>redirect_uri</c> sent to the provider, and the matching
    /// Razor page at <c>Pages/Auth/SocialCallback.cshtml</c> declares
    /// <c>@page "/auth/social/callback"</c>. Feature 115 FR-021.
    ///
    /// Regression guard: if this constant changes, the OAuth-app
    /// registrations at every provider for every environment must be
    /// updated to match. Do not change without coordinated rollout.
    /// </summary>
    public const string CallbackPath = "/auth/social/callback";

    /// <summary>
    /// Resolves the canonical callback URL for OAuth providers. Production
    /// environments behind a TLS-terminating reverse proxy (Caddy, ALB,
    /// ingress) MUST set <c>OAuth:CallbackBaseUrl</c> to the public origin
    /// (e.g. <c>https://n1.sorcha.dev</c>) — relying on
    /// <see cref="HttpRequest.Scheme"/> alone has produced
    /// <c>redirect_uri_mismatch</c> errors at Google when the proxy chain
    /// did not propagate <c>X-Forwarded-Proto</c> cleanly. When the config
    /// value is unset, falls back to <c>{Scheme}://{Host}</c> from the
    /// current request — appropriate only for local development.
    /// Feature 115 FR-021.
    /// </summary>
    private static string ResolveCallbackUrl(HttpContext httpContext, IConfiguration configuration)
    {
        var configured = configuration["OAuth:CallbackBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return $"{configured.TrimEnd('/')}{CallbackPath}";
        }

        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{CallbackPath}";
    }

    /// <summary>
    /// Maps social login endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapSocialLoginEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/social")
            .WithTags("Social Login");

        group.MapGet("/providers", ListConfiguredProviders)
            .WithName("ListSocialProviders")
            .WithSummary("List configured social providers")
            .WithDescription("Returns the social providers that have working credentials on this host. "
                + "Anonymous — drives the conditional 'Continue with…' buttons on the wallet sign-in screen.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<SocialProvidersResponse>();

        group.MapPost("/initiate", InitiateSocialFlow)
            .WithName("InitiateSocialLogin")
            .WithSummary("Start social login or link flow")
            .WithDescription("Generates an OAuth authorization URL for the specified provider. "
                + "Pass intent=login (default) for the anonymous signup/login flow, or "
                + "intent=link from a signed-in session to add the provider to the caller's "
                + "existing PlatformUser. Feature 116 / Q6.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<SocialLoginInitiateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/callback", CompleteSocialFlow)
            .WithName("CompleteSocialLogin")
            .WithSummary("Complete social login or link with authorization code")
            .WithDescription("Exchanges the authorization code for user claims and dispatches "
                + "on the intent recovered from the cached state token. login → resolve/create "
                + "PlatformUser + JWT; link → verify caller matches captured PlatformUser + "
                + "ISocialLinkService.LinkAsync.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<TokenResponse>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        // Feature 116 US1 — unlink. Challenge-gated + last-method-floor protected.
        // The orphaned POST /api/auth/social/link initiate-only endpoint was
        // removed in this PR; UI now calls /initiate directly with intent=link.
        group.MapDelete("/{linkId:guid}", UnlinkSocialProvider)
            .WithName("UnlinkSocialProvider")
            .WithSummary("Unlink a social provider from the signed-in user")
            .WithDescription("Hard-deletes the PlatformSocialLogin row. Requires a valid "
                + "X-Auth-Challenge header scoped to RemoveAuthMethod. Returns 409 if removing "
                + "would leave the user with zero sign-in methods. Feature 116 US1.")
            .RequireAuthorization()
            .RequireAuthChallenge(ScopedOperation.RemoveAuthMethod)
            .RequireRateLimiting("platform-auth")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    private static IResult ListConfiguredProviders(ISocialLoginService socialLoginService)
        => ListConfiguredProvidersForTest(socialLoginService);

    /// <summary>Test seam for <see cref="ListConfiguredProviders"/> (no HttpContext needed).</summary>
    internal static Microsoft.AspNetCore.Http.HttpResults.Ok<SocialProvidersResponse> ListConfiguredProvidersForTest(
        ISocialLoginService socialLoginService)
        => TypedResults.Ok(new SocialProvidersResponse
        {
            Providers = socialLoginService.GetConfiguredProviderNames()
        });

    /// <summary>
    /// POST /api/auth/social/initiate — start a social login or link flow.
    /// Feature 116 / Q6: intent=link requires authentication and captures the
    /// caller's PlatformUserId into the cached state for callback validation.
    /// </summary>
    private static async Task<IResult> InitiateSocialFlow(
        SocialLoginInitiateRequest request,
        ISocialLoginService socialLoginService,
        IPlatformSettingsService platformSettingsService,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate provider
        var validProviders = new[] { "google", "github", "microsoft", "apple" };
        if (string.IsNullOrWhiteSpace(request.Provider) ||
            !validProviders.Contains(request.Provider.ToLowerInvariant()))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = [$"Provider must be one of: {string.Join(", ", validProviders)}"]
            });
        }

        // Validate intent. Empty / null defaults to "login" for backward-compat
        // with the existing public-signup callers. Anything else is a 400.
        var intent = ParseIntent(request.Intent);
        if (intent is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["intent"] = ["intent must be 'login' or 'link'"]
            });
        }

        // For link flow: require authentication and capture the caller's
        // PlatformUserId into the cached state. The callback handler will
        // verify the active bearer matches this id before persisting.
        Guid? targetPlatformUserId = null;
        if (intent == SocialFlowIntent.Link)
        {
            var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                           ?? httpContext.User.FindFirst("pid")?.Value;
            if (!Guid.TryParse(pidClaim, out var pid))
            {
                return TypedResults.Unauthorized();
            }
            targetPlatformUserId = pid;
        }
        else
        {
            // Existing constraint: public-org must be enabled for the anonymous
            // signup/login flow. Link is a no-op against PublicOrg state — it
            // adds a method to an existing PlatformUser regardless of whether
            // the public org is currently accepting new signups.
            var settings = await platformSettingsService.GetAsync(ct);
            if (!settings.PublicOrgEnabled)
            {
                return TypedResults.Problem(
                    "Public organisation is not enabled. Social login is unavailable.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // Validate + normalise surface. null / missing = existing /app web flow.
        // "wallet" routes the post-OAuth callback into the citizen wallet PWA.
        var surface = string.IsNullOrWhiteSpace(request.Surface) ? null : request.Surface.Trim().ToLowerInvariant();
        if (surface is not (null or "wallet" or "app"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["surface"] = ["surface must be 'wallet' or 'app'"]
            });
        }

        // Resolve callback URL (config-first, falls back to Request scheme/host).
        // See ResolveCallbackUrl for why config-first is required behind a
        // TLS-terminating reverse proxy.
        var redirectUri = ResolveCallbackUrl(httpContext, configuration);

        try
        {
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, redirectUri, intent.Value, targetPlatformUserId, surface, ct);

            logger.LogInformation(
                "Social {Intent} flow initiated for provider {Provider}",
                intent.Value, request.Provider);

            return TypedResults.Ok(new SocialLoginInitiateResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social initiate failed: provider not configured");
            return TypedResults.Problem(
                $"Social provider '{request.Provider}' is not configured.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static SocialFlowIntent? ParseIntent(string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent)) return SocialFlowIntent.Login;
        return intent.Trim().ToLowerInvariant() switch
        {
            "login" => SocialFlowIntent.Login,
            "link" => SocialFlowIntent.Link,
            _ => null,
        };
    }

    /// <summary>
    /// POST /api/auth/social/callback — complete a social login or link flow.
    /// Feature 116 / Q6: dispatches on the cached intent. login → existing
    /// resolve-or-create + JWT path; link → verify caller matches the
    /// captured PlatformUser id then call ISocialLinkService.LinkAsync.
    /// </summary>
    private static async Task<IResult> CompleteSocialFlow(
        SocialLoginCallbackRequest request,
        ISocialLoginService socialLoginService,
        ISocialLinkService socialLinkService,
        IPlatformUserService platformUserService,
        IPlatformSettingsService platformSettingsService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        TenantDbContext db,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate required fields
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code))
            errors["code"] = ["Authorization code is required"];
        if (string.IsNullOrWhiteSpace(request.State))
            errors["state"] = ["State parameter is required"];
        if (string.IsNullOrWhiteSpace(request.Provider))
            errors["provider"] = ["Provider is required"];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        // Exchange code for claims
        var callbackResult = await socialLoginService.ExchangeCodeAsync(
            request.Provider, request.Code, request.State, ct);

        if (!callbackResult.Success)
        {
            logger.LogWarning("Social {Intent} callback failed for {Provider}: {Error}",
                callbackResult.Intent, request.Provider, callbackResult.Error);
            return TypedResults.Problem(
                callbackResult.Error ?? "Social login failed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrEmpty(callbackResult.Subject))
        {
            return TypedResults.Problem(
                "Could not determine user identity from provider.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Feature 116 dispatch: link flow short-circuits before the
        // resolve-or-create path. Returns 204 on success — no JWT issued
        // because the caller is already authenticated.
        if (callbackResult.Intent == SocialFlowIntent.Link)
        {
            return await HandleLinkCallbackAsync(
                callbackResult, socialLinkService, httpContext, logger, ct);
        }

        // Resolve or create PlatformUser under the strict link policy (feature 115).
        // Use the resolved provider name from the callback result throughout — it's
        // been validated against the cached state and is the same value used for
        // metrics tagging and logging. Defence-in-depth: never reflect raw
        // request input back into a user-visible message.
        var resolveResult = await platformUserService.ResolveOrCreateSocialUserAsync(callbackResult, ct);
        if (resolveResult.Refusal != SocialLoginRefusal.None)
        {
            var resolvedProvider = callbackResult.Provider;
            var problemMessage = resolveResult.Refusal switch
            {
                SocialLoginRefusal.ProviderUnverified =>
                    $"Your {resolvedProvider} account hasn't verified this email address. Please verify it with the provider and try again.",
                SocialLoginRefusal.ExistingUnverified =>
                    "An account exists for this email but isn't verified. Sign in with your password and verify your email first.",
                _ => "Social login was refused.",
            };

            SocialLoginMetrics.RecordRefusal(resolvedProvider, resolveResult.Refusal);
            logger.LogWarning(
                "Social login refused via API: provider={Provider}, reason={Reason}",
                resolvedProvider, resolveResult.Refusal);

            return TypedResults.Problem(problemMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        var platformUser = resolveResult.User!;
        var isNew = resolveResult.IsNew;

        // Ensure UserIdentity exists in the public org
        var publicOrgId = WellKnownIds.PublicOrgId;
        var userIdentity = await db.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.OrganizationId == publicOrgId, ct);

        if (userIdentity is null)
        {
            userIdentity = new UserIdentity
            {
                OrganizationId = publicOrgId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email,
                DisplayName = platformUser.DisplayName,
                Roles = [UserRole.Consumer],
                Status = IdentityStatus.Active,
                ProvisionedVia = ProvisioningMethod.SocialLogin,
                ProfileCompleted = !string.IsNullOrWhiteSpace(platformUser.Email)
                    && !string.IsNullOrWhiteSpace(platformUser.DisplayName)
            };

            await identityRepository.CreateUserAsync(userIdentity, ct);

            logger.LogInformation("Created UserIdentity {UserIdentityId} in public org for PlatformUser {PlatformUserId}",
                userIdentity.Id, platformUser.Id);
        }

        // Ensure PlatformUserOrgMembership exists
        var memberships = await platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
        if (!memberships.Any(m => m.OrganizationId == publicOrgId))
        {
            await platformUserService.AddOrgMembershipAsync(
                platformUser.Id, publicOrgId, UserRole.Consumer.ToString(), ct);
        }

        // Update last login
        userIdentity.LastLoginAt = DateTimeOffset.UtcNow;
        await identityRepository.UpdateUserAsync(userIdentity, ct);

        // Get the public org for token generation
        var publicOrg = await organizationRepository.GetByIdAsync(publicOrgId, ct);
        if (publicOrg is null)
        {
            return TypedResults.Problem(
                "Public organisation not found.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Issue JWT
        var tokenResponse = await tokenService.GenerateUserTokenAsync(
            userIdentity, publicOrg, platformUser.Id, cancellationToken: ct);

        logger.LogInformation(
            "Social login completed for PlatformUser {PlatformUserId} via {Provider} (isNew={IsNew})",
            platformUser.Id, request.Provider, isNew);

        return TypedResults.Ok(tokenResponse);
    }

    /// <summary>
    /// Handles the link branch of <see cref="CompleteSocialFlow"/>. Verifies
    /// the active bearer matches the PlatformUser id captured at initiate
    /// time, then delegates to <see cref="ISocialLinkService.LinkAsync"/>.
    /// Returns 204 on success, 401 on session-swap, 409 on collision.
    /// </summary>
    private static async Task<IResult> HandleLinkCallbackAsync(
        SocialAuthCallbackResult callbackResult,
        ISocialLinkService socialLinkService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Defence against session swap mid-flight: the PlatformUser id captured
        // when /initiate ran must match the bearer that returns to /callback.
        if (callbackResult.TargetPlatformUserId is null)
        {
            logger.LogWarning("Social link callback: cached state lacked TargetPlatformUserId");
            return TypedResults.Problem(
                "Link state is invalid.", statusCode: StatusCodes.Status400BadRequest);
        }

        var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                       ?? httpContext.User.FindFirst("pid")?.Value;
        if (!Guid.TryParse(pidClaim, out var callerPlatformUserId))
        {
            return TypedResults.Unauthorized();
        }

        if (callerPlatformUserId != callbackResult.TargetPlatformUserId.Value)
        {
            logger.LogWarning(
                "Social link callback: bearer {Caller} does not match captured target {Target}",
                callerPlatformUserId, callbackResult.TargetPlatformUserId);
            return TypedResults.Unauthorized();
        }

        var outcome = await socialLinkService.LinkAsync(
            callerPlatformUserId,
            callbackResult.Provider,
            callbackResult.Subject!,
            callbackResult.Email,
            callbackResult.DisplayName,
            ct);

        return outcome switch
        {
            SocialLinkOutcome.Linked => TypedResults.NoContent(),
            SocialLinkOutcome.AlreadyLinkedToCaller => TypedResults.NoContent(),
            SocialLinkOutcome.AlreadyLinkedToDifferentUser => TypedResults.Problem(
                $"This {callbackResult.Provider} account is linked to a different Sorcha account.",
                statusCode: StatusCodes.Status409Conflict),
            SocialLinkOutcome.EmailCollision => TypedResults.Problem(
                $"This {callbackResult.Provider} account uses an email that belongs to a different Sorcha account.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(
                "Social link failed.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE /api/auth/social/{linkId} — unlink a social provider from the
    /// signed-in user (Feature 116 US1). Challenge-gated via the
    /// <see cref="RequireAuthChallengeAttribute"/> filter on the route map;
    /// last-method floor enforced server-side via
    /// <see cref="IAuthMethodService.WouldRemovingLeaveZeroAsync"/>.
    /// </summary>
    private static async Task<IResult> UnlinkSocialProvider(
        Guid linkId,
        ISocialLinkService socialLinkService,
        IIdentityRepository identityRepository,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                       ?? httpContext.User.FindFirst("pid")?.Value;
        Guid platformUserId;
        if (Guid.TryParse(pidClaim, out var pidFromClaim))
        {
            platformUserId = pidFromClaim;
        }
        else
        {
            // Fallback for sessions that pre-date the platform_user_id claim:
            // resolve via UserIdentity.Id from the sub claim.
            var sub = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? httpContext.User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var userIdentityId))
            {
                return TypedResults.Unauthorized();
            }
            var user = await identityRepository.GetUserByIdAsync(userIdentityId, ct);
            if (user is null || user.PlatformUserId == Guid.Empty)
            {
                return TypedResults.Unauthorized();
            }
            platformUserId = user.PlatformUserId;
        }

        var outcome = await socialLinkService.UnlinkAsync(platformUserId, linkId, ct);
        return outcome switch
        {
            SocialUnlinkOutcome.Unlinked => TypedResults.NoContent(),
            SocialUnlinkOutcome.NotFound => TypedResults.NotFound(),
            SocialUnlinkOutcome.FloorViolation => TypedResults.Problem(
                "You must keep at least one sign-in method.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Problem(
                "Unlink failed.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
