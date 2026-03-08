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
/// OIDC authentication flow endpoints: initiate, callback, profile completion.
/// Handles the full authorization code + PKCE exchange, user provisioning, and JWT issuance.
/// </summary>
public static class OidcEndpoints
{
    /// <summary>
    /// Maps OIDC authentication endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapOidcEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("OIDC Authentication");

        // Initiate OIDC login flow (public)
        group.MapPost("/oidc/initiate", PostInitiate)
            .WithName("OidcInitiate")
            .WithSummary("Initiate OIDC login flow")
            .WithDescription("Generates an authorization URL for the organization's configured IDP. "
                + "The client should redirect the user to this URL to begin the OIDC login flow.")
            .AllowAnonymous()
            .Produces<OidcInitiateResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        // OIDC callback — exchange code for tokens (public)
        group.MapGet("/callback/{orgSubdomain}", GetCallback)
            .WithName("OidcCallback")
            .WithSummary("OIDC callback — exchange authorization code for Sorcha JWT")
            .WithDescription("Receives the authorization code from the external IDP, exchanges it for tokens, "
                + "provisions or matches the user, and returns a Sorcha JWT.")
            .AllowAnonymous()
            .Produces<OidcCallbackResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        // Complete user profile after OIDC provisioning (authenticated)
        group.MapPost("/oidc/complete-profile", PostCompleteProfile)
            .WithName("OidcCompleteProfile")
            .WithSummary("Complete user profile after OIDC login")
            .WithDescription("Updates missing profile fields (display name, email) for a user provisioned via OIDC "
                + "whose IDP did not return all required claims.")
            .RequireAuthorization()
            .Produces<OidcCallbackResult>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Email verification (public)
        group.MapPost("/verify-email", PostVerifyEmail)
            .WithName("VerifyEmail")
            .WithSummary("Verify email address with token")
            .WithDescription("Validates an email verification token and marks the user's email as verified.")
            .AllowAnonymous()
            .Produces<EmailVerificationResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        // Resend verification email (authenticated, rate limited)
        group.MapPost("/resend-verification", PostResendVerification)
            .WithName("ResendVerification")
            .WithSummary("Resend email verification")
            .WithDescription("Resends an email verification token. Rate limited to 3 per hour per user.")
            .RequireAuthorization()
            .Produces<EmailVerificationResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    /// <summary>
    /// POST /api/auth/oidc/initiate — resolves org by subdomain, generates OIDC authorization URL.
    /// </summary>
    private static async Task<IResult> PostInitiate(
        OidcInitiateRequest request,
        IOidcExchangeService exchangeService,
        TenantDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrgSubdomain))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["orgSubdomain"] = ["Organization subdomain is required"]
            });
        }

        // Resolve organization by subdomain
        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Subdomain == request.OrgSubdomain, cancellationToken);

        if (org is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var result = await exchangeService.GenerateAuthorizationUrlAsync(
                org.Id, request.RedirectUrl, cancellationToken);

            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to initiate OIDC for org {OrgSubdomain}", request.OrgSubdomain);
            return TypedResults.NotFound();
        }
    }

    /// <summary>
    /// GET /api/auth/callback/{orgSubdomain}?code=...&amp;state=... — OIDC callback handler.
    /// Exchanges authorization code, provisions/matches user, issues Sorcha JWT.
    /// </summary>
    private static async Task<IResult> GetCallback(
        string orgSubdomain,
        string? code,
        string? state,
        IOidcExchangeService exchangeService,
        IOidcProvisioningService provisioningService,
        ITokenService tokenService,
        ITotpService totpService,
        TenantDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Validate required query parameters
        if (string.IsNullOrWhiteSpace(code))
        {
            return TypedResults.BadRequest(new OidcCallbackResult
            {
                Success = false,
                Error = "Missing authorization code."
            });
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            return TypedResults.BadRequest(new OidcCallbackResult
            {
                Success = false,
                Error = "Missing state parameter."
            });
        }

        // Exchange authorization code for tokens
        OidcCallbackResult exchangeResult;
        try
        {
            exchangeResult = await exchangeService.ExchangeCodeAsync(
                code, state, orgSubdomain, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "OIDC code exchange failed for {OrgSubdomain}", orgSubdomain);
            return TypedResults.BadRequest(new OidcCallbackResult
            {
                Success = false,
                Error = ex.Message
            });
        }

        if (!exchangeResult.Success)
        {
            return TypedResults.BadRequest(exchangeResult);
        }

        // Resolve org for provisioning
        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Subdomain == orgSubdomain, cancellationToken);

        if (org is null)
        {
            return TypedResults.BadRequest(new OidcCallbackResult
            {
                Success = false,
                Error = "Organization not found."
            });
        }

        // Extract claims from the ID token (already validated in ExchangeCodeAsync)
        // Re-validate to get claims — the exchange service validated but we need the claims
        var idpConfig = await dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == org.Id && c.IsEnabled, cancellationToken);

        if (idpConfig is null)
        {
            return TypedResults.BadRequest(new OidcCallbackResult
            {
                Success = false,
                Error = "No IDP configured for this organization."
            });
        }

        // For the callback flow, the ExchangeCodeAsync already validated the token.
        // We need to provision the user. The exchange service should expose the claims.
        // Since ExchangeCodeAsync currently returns a simple result, we'll re-extract from the flow.
        // TODO: Refactor ExchangeCodeAsync to return claims alongside the result.

        // For now, provision with the exchange result's userId if available,
        // or create a minimal claims set from the callback
        var callbackResult = new OidcCallbackResult
        {
            Success = true,
            UserId = exchangeResult.UserId,
            IsFirstLogin = exchangeResult.IsFirstLogin
        };

        // Generate Sorcha JWT
        if (callbackResult.UserId.HasValue)
        {
            var user = await dbContext.UserIdentities
                .FirstOrDefaultAsync(u => u.Id == callbackResult.UserId, cancellationToken);

            if (user is not null)
            {
                // Check if 2FA is required
                var totpStatus = await totpService.GetStatusAsync(user.Id, cancellationToken);
                if (totpStatus.IsEnabled)
                {
                    var loginToken = await totpService.GenerateLoginTokenAsync(user.Id, cancellationToken);
                    return TypedResults.Ok(new OidcCallbackResult
                    {
                        Success = true,
                        Requires2FA = true,
                        PartialToken = loginToken,
                        UserId = user.Id,
                        IsFirstLogin = callbackResult.IsFirstLogin
                    });
                }

                // Check if profile completion is needed
                var needsProfile = await provisioningService.DetermineProfileCompletionAsync(user);
                if (needsProfile)
                {
                    return TypedResults.Ok(new OidcCallbackResult
                    {
                        Success = true,
                        RequiresProfileCompletion = true,
                        UserId = user.Id,
                        IsFirstLogin = callbackResult.IsFirstLogin
                    });
                }

                // Log audit event
                dbContext.AuditLogEntries.Add(new AuditLogEntry
                {
                    EventType = callbackResult.IsFirstLogin
                        ? AuditEventType.OidcFirstLogin
                        : AuditEventType.Login,
                    IdentityId = user.Id,
                    OrganizationId = org.Id,
                    Timestamp = DateTimeOffset.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);

                // Issue full JWT
                var tokenResponse = await tokenService.GenerateUserTokenAsync(user, org, cancellationToken);
                return TypedResults.Ok(new OidcCallbackResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn,
                    UserId = user.Id,
                    IsFirstLogin = callbackResult.IsFirstLogin
                });
            }
        }

        return TypedResults.Ok(callbackResult);
    }

    /// <summary>
    /// POST /api/auth/oidc/complete-profile — updates missing profile fields.
    /// </summary>
    private static async Task<IResult> PostCompleteProfile(
        OidcCompleteProfileRequest request,
        ClaimsPrincipal principal,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var user = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Update profile fields
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrWhiteSpace(request.Email))
            user.Email = request.Email;

        user.ProfileCompleted = !string.IsNullOrWhiteSpace(user.Email)
            && !string.IsNullOrWhiteSpace(user.DisplayName);

        await identityRepository.UpdateUserAsync(user, cancellationToken);

        // Audit profile completion
        logger.LogInformation(
            "Profile completed for user {UserId}",
            user.Id);

        // Return updated result
        var org = await organizationRepository.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (org is not null)
        {
            var tokenResponse = await tokenService.GenerateUserTokenAsync(user, org, cancellationToken);
            return TypedResults.Ok(new OidcCallbackResult
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresIn = tokenResponse.ExpiresIn,
                UserId = user.Id
            });
        }

        return TypedResults.Ok(new OidcCallbackResult
        {
            Success = true,
            UserId = user.Id
        });
    }

    /// <summary>
    /// POST /api/auth/verify-email — validates token and marks email as verified.
    /// </summary>
    private static async Task<IResult> PostVerifyEmail(
        VerifyEmailRequest request,
        IEmailVerificationService verificationService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return TypedResults.BadRequest(new EmailVerificationResponse
            {
                Success = false,
                Message = "Verification token is required."
            });
        }

        var (success, error) = await verificationService.VerifyTokenAsync(request.Token, cancellationToken);

        if (!success)
        {
            return TypedResults.BadRequest(new EmailVerificationResponse
            {
                Success = false,
                Message = error
            });
        }

        return TypedResults.Ok(new EmailVerificationResponse
        {
            Success = true,
            Message = "Email verified successfully."
        });
    }

    /// <summary>
    /// POST /api/auth/resend-verification — resends verification email (rate limited).
    /// </summary>
    private static async Task<IResult> PostResendVerification(
        ResendVerificationRequest request,
        ClaimsPrincipal principal,
        IEmailVerificationService verificationService,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var user = await identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Rate limit check
        var canResend = await verificationService.CanResendAsync(userId, cancellationToken);
        if (!canResend)
        {
            return TypedResults.Problem(
                "Too many verification requests. Please try again later.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        await verificationService.GenerateAndSendVerificationAsync(user, cancellationToken);

        return TypedResults.Ok(new EmailVerificationResponse
        {
            Success = true,
            Message = "Verification email sent."
        });
    }
}
