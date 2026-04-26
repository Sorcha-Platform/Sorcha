// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
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
    /// Maps social login endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapSocialLoginEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/social")
            .WithTags("Social Login");

        group.MapPost("/initiate", InitiateSocialLogin)
            .WithName("InitiateSocialLogin")
            .WithSummary("Start social login flow")
            .WithDescription("Generates an OAuth authorization URL for the specified provider. "
                + "The public organisation must be enabled and the provider must be configured.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<SocialLoginInitiateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/callback", CompleteSocialLogin)
            .WithName("CompleteSocialLogin")
            .WithSummary("Complete social login with authorization code")
            .WithDescription("Exchanges the authorization code for user claims, resolves or creates a PlatformUser, "
                + "creates UserIdentity in the public org if new, and issues a JWT.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/link", LinkSocialProvider)
            .WithName("LinkSocialProvider")
            .WithSummary("Link additional social provider to current user")
            .WithDescription("Initiates an OAuth flow for linking an additional social provider "
                + "to the currently authenticated PlatformUser.")
            .RequireAuthorization()
            .Produces<SocialLoginInitiateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// POST /api/auth/social/initiate — start social login flow.
    /// </summary>
    private static async Task<IResult> InitiateSocialLogin(
        SocialLoginInitiateRequest request,
        ISocialLoginService socialLoginService,
        IPlatformSettingsService platformSettingsService,
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

        // Check public org is enabled
        var settings = await platformSettingsService.GetAsync(ct);
        if (!settings.PublicOrgEnabled)
        {
            return TypedResults.Problem(
                "Public organisation is not enabled. Social login is unavailable.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Build redirect URI for the callback. This MUST match the registered
        // redirect URI at the OAuth provider (see docs/guides/SOCIAL-LOGIN-SETUP.md).
        // Single canonical path per environment per feature 115 FR-021.
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var redirectUri = $"{baseUrl}/auth/social/callback";

        try
        {
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, redirectUri, ct);

            logger.LogInformation("Social login initiated for provider {Provider}", request.Provider);

            return TypedResults.Ok(new SocialLoginInitiateResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social login initiate failed: provider not configured");
            return TypedResults.Problem(
                $"Social provider '{request.Provider}' is not configured.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// POST /api/auth/social/callback — complete social login with authorization code.
    /// </summary>
    private static async Task<IResult> CompleteSocialLogin(
        SocialLoginCallbackRequest request,
        ISocialLoginService socialLoginService,
        IPlatformUserService platformUserService,
        IPlatformSettingsService platformSettingsService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        TenantDbContext db,
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
            logger.LogWarning("Social login callback failed for {Provider}: {Error}",
                request.Provider, callbackResult.Error);
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

        // Resolve or create PlatformUser under the strict link policy (feature 115)
        var resolveResult = await platformUserService.ResolveOrCreateSocialUserAsync(callbackResult, ct);
        if (resolveResult.Refusal != SocialLoginRefusal.None)
        {
            var problemMessage = resolveResult.Refusal switch
            {
                SocialLoginRefusal.ProviderUnverified =>
                    $"Your {request.Provider} account hasn't verified this email address. Please verify it with the provider and try again.",
                SocialLoginRefusal.ExistingUnverified =>
                    "An account exists for this email but isn't verified. Sign in with your password and verify your email first.",
                _ => "Social login was refused.",
            };

            SocialLoginMetrics.RecordRefusal(request.Provider, resolveResult.Refusal);
            logger.LogWarning(
                "Social login refused via API: provider={Provider}, reason={Reason}",
                request.Provider, resolveResult.Refusal);

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
            userIdentity, publicOrg, platformUser.Id, ct);

        logger.LogInformation(
            "Social login completed for PlatformUser {PlatformUserId} via {Provider} (isNew={IsNew})",
            platformUser.Id, request.Provider, isNew);

        return TypedResults.Ok(tokenResponse);
    }

    /// <summary>
    /// POST /api/auth/social/link — link additional social provider to current user.
    /// </summary>
    private static async Task<IResult> LinkSocialProvider(
        SocialLoginInitiateRequest request,
        ISocialLoginService socialLoginService,
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

        // Verify user is authenticated
        var platformUserIdClaim = httpContext.User.FindFirst("platform_user_id")?.Value;
        if (string.IsNullOrEmpty(platformUserIdClaim))
        {
            return TypedResults.Unauthorized();
        }

        // Build redirect URI
        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var redirectUri = $"{baseUrl}/api/auth/social/callback-redirect";

        try
        {
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, redirectUri, ct);

            logger.LogInformation(
                "Social provider link initiated for PlatformUser {PlatformUserId}, provider {Provider}",
                platformUserIdClaim, request.Provider);

            return TypedResults.Ok(new SocialLoginInitiateResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social provider link failed: provider not configured");
            return TypedResults.Problem(
                $"Social provider '{request.Provider}' is not configured.",
                statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
