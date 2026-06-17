// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

#pragma warning disable ASPDEPR002 // WithOpenApi is deprecated; using it for co-located endpoint examples until transformer API stabilizes

using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Authentication and token management API endpoints.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Maps authentication endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        // Login with email/password (public endpoint)
        // Returns TokenResponse on success, or TwoFactorLoginResponse if TOTP 2FA is enabled
        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Login with email and password")
            .WithDescription("Authenticates a user with email and password. Returns access/refresh tokens on success, "
                + "or a loginToken with requiresTwoFactor=true if the user has TOTP 2FA enabled.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .Produces<TwoFactorLoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "email": "admin@acme.corp",
                      "password": "SecureP@ss123",
                      "organizationSubdomain": "acme-corp"
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
                      "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
                      "token_type": "Bearer",
                      "expires_in": 3600,
                      "scope": "openid profile email"
                    }
                    """);
                return operation;
            });

        // Verify 2FA code after login (public endpoint — uses loginToken)
        group.MapPost("/verify-2fa", Verify2Fa)
            .WithName("Verify2Fa")
            .WithSummary("Verify a 2FA code (TOTP, backup, or email) to complete login")
            .WithDescription("Accepts a loginToken (from login response) and a code. Set method=email "
                + "for an emailed one-time code, otherwise a TOTP or backup code is assumed. "
                + "Returns access/refresh tokens on successful verification.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpEndpoints.TotpRateLimitPolicy)
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Feature 150 US2 — send (or resend) an email one-time code mid-login. The "use another
        // method" fallback: usable even when a stronger factor is the primary. loginToken-gated.
        group.MapPost("/login/2fa/send-email", SendEmailLoginCode)
            .WithName("SendEmailLoginCode")
            .WithSummary("Send an email one-time code during login")
            .WithDescription("Given a valid loginToken, dispatches an emailed one-time code so the user "
                + "can complete 2FA with email. Rate-limited (429 within the send cooldown).")
            .AllowAnonymous()
            .RequireRateLimiting(TotpEndpoints.TotpRateLimitPolicy)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized);

        // Complete login after org selection (public endpoint)
        group.MapPost("/select-org", SelectOrg)
            .WithName("SelectOrg")
            .WithSummary("Complete login by selecting an organisation")
            .WithDescription("After a login response with requires_org_selection=true, submit the platform login token "
                + "and chosen organisation ID to receive access/refresh tokens.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .Produces<TwoFactorLoginResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Token refresh (public endpoint - requires valid refresh token)
        group.MapPost("/token/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithSummary("Refresh access token")
            .WithDescription("Exchanges a valid refresh token for a new access token.")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4..."
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
                      "refresh_token": "bmV3IHJlZnJlc2ggdG9rZW4...",
                      "token_type": "Bearer",
                      "expires_in": 3600,
                      "scope": "openid profile email"
                    }
                    """);
                return operation;
            });

        // Token revocation (requires authentication)
        group.MapPost("/token/revoke", RevokeToken)
            .WithName("RevokeToken")
            .WithSummary("Revoke a token")
            .WithDescription("Revokes an access or refresh token, preventing future use.")
            .RequireAuthorization()
            .Produces<SuccessResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Token introspection (service-to-service)
        group.MapPost("/token/introspect", IntrospectToken)
            .WithName("IntrospectToken")
            .WithSummary("Introspect a token")
            .WithDescription("Returns information about a token, including whether it is active. Service tokens only.")
            .RequireAuthorization("RequireService")
            .Produces<TokenIntrospectionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Revoke all user tokens (admin only)
        group.MapPost("/token/revoke-user", RevokeUserTokens)
            .WithName("RevokeUserTokens")
            .WithSummary("Revoke all tokens for a user")
            .WithDescription("Revokes all access and refresh tokens for a specific user. Administrator only.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<SuccessResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Revoke all organization tokens (admin only)
        group.MapPost("/token/revoke-organization", RevokeOrganizationTokens)
            .WithName("RevokeOrganizationTokens")
            .WithSummary("Revoke all tokens for an organization")
            .WithDescription("Revokes all access and refresh tokens for all users in an organization. Administrator only.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<SuccessResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Current user info
        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Get current user information")
            .WithDescription("Returns information about the currently authenticated user from their token claims.")
            .RequireAuthorization()
            .Produces<CurrentUserResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetResponseExample(operation, "200", """
                    {
                      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "email": "admin@acme.corp",
                      "displayName": "Alice Admin",
                      "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
                      "organizationName": "Acme Corporation",
                      "roles": ["Administrator"],
                      "tokenType": "user",
                      "scopes": ["openid", "profile", "email"],
                      "authMethod": "password"
                    }
                    """);
                return operation;
            });

        // Self-registration with email/password (public endpoint)
        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Self-register with email/password")
            .WithDescription("Creates a local account for public organizations with self-registration enabled. "
                + "Validates password against NIST policy and HIBP breach list. Sends verification email.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<SelfRegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .WithOpenApi(operation =>
            {
                OpenApiExamples.SetRequestExample(operation, """
                    {
                      "orgSubdomain": "public",
                      "email": "user@example.com",
                      "password": "MySecureP@ss456",
                      "displayName": "Jane Doe"
                    }
                    """);
                OpenApiExamples.SetResponseExample(operation, "201", """
                    {
                      "success": true,
                      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
                      "message": "Verification email sent to user@example.com"
                    }
                    """);
                return operation;
            });

        // Passkey 2FA: get assertion options (public endpoint — uses loginToken)
        group.MapPost("/verify-passkey/options", VerifyPasskeyOptions)
            .WithName("VerifyPasskeyOptions")
            .WithSummary("Get passkey assertion options for 2FA login")
            .WithDescription("Validates the login token and returns FIDO2 assertion options scoped to the user's registered passkeys. "
                + "The returned options and transaction ID are used in the verify-passkey endpoint.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpEndpoints.TotpRateLimitPolicy)
            .Produces<PasskeyAssertionOptionsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Passkey 2FA: verify assertion response (public endpoint — uses loginToken)
        group.MapPost("/verify-passkey", VerifyPasskey)
            .WithName("VerifyPasskey")
            .WithSummary("Verify passkey assertion to complete 2FA login")
            .WithDescription("Verifies the passkey assertion response and issues access/refresh tokens on success. "
                + "Requires a valid login token from the initial login response.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpEndpoints.TotpRateLimitPolicy)
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // Logout (revokes current token)
        group.MapPost("/logout", Logout)
            .WithName("Logout")
            .WithSummary("Logout and revoke current token")
            .WithDescription("Logs out the current user by revoking their access token.")
            .RequireAuthorization()
            .Produces<SuccessResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // List user's organisations (for org switcher)
        group.MapGet("/me/organizations", GetMyOrganizations)
            .WithName("GetMyOrganizations")
            .WithSummary("List organisations the current user belongs to")
            .WithDescription("Returns all org memberships for the authenticated PlatformUser. "
                + "Used by the org switcher UI. Includes org name, subdomain, role, and isCurrent flag.")
            .RequireAuthorization()
            .Produces<OrgMembershipListResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        // Switch active organisation
        group.MapPost("/switch-org", SwitchOrganization)
            .WithName("SwitchOrganization")
            .WithSummary("Switch active organisation context")
            .WithDescription("Issues a new JWT scoped to the target org. Validates the user has "
                + "a PlatformUserOrgMembership for the target org and an active UserIdentity.")
            .RequireAuthorization()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Create organisation (self-service, authenticated)
        group.MapPost("/create-org", CreateOrganization)
            .WithName("CreateOrganizationSelfService")
            .WithSummary("Create a new private organisation")
            .WithDescription("Self-service org creation for public org members. Validates eligibility "
                + "(email verified, within org limit, subdomain available) and atomically provisions "
                + "the org with the caller as admin.")
            .RequireAuthorization()
            .Produces<OrgProvisioningResult>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        ILoginService loginService,
        Microsoft.Extensions.Options.IOptions<Models.ReturnToAllowlistOptions> returnToAllowlist,
        CancellationToken cancellationToken)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["Email is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password is required"]
            });
        }

        // Spec 136: derive the preferred trust tier — explicit hint wins, else returnTo
        // (/wallet ⇒ consumer), else Platform (the /app SPA host). An explicit hint is a hard
        // request (refused if un-entitled, FR-008); a returnTo-derived preference downgrades to
        // entitlement (a citizen on /app → consumer). The tier follows the person.
        var explicitTier = RequestedTierResolver.ParseTierHint(request.Tier);
        var preferredTier = explicitTier
            ?? RequestedTierResolver.ClassifyReturnTo(request.ReturnTo, returnToAllowlist.Value)
            ?? Sorcha.ServiceDefaults.Auth.Tier.Platform;
        var tierExplicit = explicitTier is not null;

        var result = !string.IsNullOrWhiteSpace(request.OrganizationSubdomain)
            ? await loginService.LoginAsync(request.Email, request.Password, request.OrganizationSubdomain, preferredTier, tierExplicit, cancellationToken)
            : await loginService.LoginAsync(request.Email, request.Password, preferredTier, tierExplicit, cancellationToken);

        if (!result.Success)
        {
            // Rate-limited responses get 429
            if (result.ErrorCode == LoginErrorCode.RateLimited)
            {
                return TypedResults.Problem(result.Error,
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            // Explicit over-request of a tier the holder is not entitled to (spec 136, FR-008).
            if (result.ErrorCode == LoginErrorCode.TierNotEntitled)
            {
                return TypedResults.Problem(result.Error,
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return TypedResults.Unauthorized();
        }

        // Org selection required: return org list with platform login token
        if (result.OrgSelectionRequired)
        {
            return TypedResults.Ok(new OrgSelectionResponse
            {
                PlatformLoginToken = result.PlatformLoginToken!,
                Organizations = result.AvailableOrganizations!
                    .Select(o => new OrgSelectionEntry
                    {
                        OrganizationId = o.OrganizationId,
                        Name = o.OrganizationName,
                        Subdomain = o.Subdomain,
                        Role = o.Role
                    })
                    .ToList()
            });
        }

        // 2FA required: return login token and available methods
        if (result.TwoFactorRequired)
        {
            return TypedResults.Ok(new TwoFactorLoginResponse
            {
                LoginToken = result.LoginToken!,
                AvailableMethods = result.AvailableMethods!.ToArray()
            });
        }

        // Standard login: return tokens
        return TypedResults.Ok(result.Tokens);
    }

    private static async Task<IResult> SelectOrg(
        CompleteOrgSelectionRequest request,
        ILoginService loginService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlatformLoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["platform_login_token"] = ["Platform login token is required"]
            });
        }

        if (request.OrganizationId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organization_id"] = ["Organization ID is required"]
            });
        }

        var result = await loginService.CompleteOrgSelectionAsync(
            request.PlatformLoginToken, request.OrganizationId, ct: cancellationToken);

        if (!result.Success)
        {
            return TypedResults.Unauthorized();
        }

        if (result.TwoFactorRequired)
        {
            return TypedResults.Ok(new TwoFactorLoginResponse
            {
                LoginToken = result.LoginToken!,
                AvailableMethods = result.AvailableMethods!.ToArray()
            });
        }

        return TypedResults.Ok(result.Tokens);
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> Verify2Fa(
        Verify2FaRequest request,
        ITotpService totpService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        IVerificationChannelRegistry channels,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.LoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["login_token"] = ["Login token is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["TOTP code or backup code is required"]
            });
        }

        // Validate login token
        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null)
        {
            logger.LogWarning("2FA verification failed: invalid or expired login token");
            return TypedResults.Unauthorized();
        }

        // Look up the identity up-front — the email channel keys off the account-wide PlatformUserId
        // and we need the active check regardless of method.
        var user = await identityRepository.GetUserByIdAsync(userId.Value, cancellationToken);
        if (user is null || user.Status != IdentityStatus.Active)
        {
            return TypedResults.Unauthorized();
        }

        // Validate the code by method: server-sent code (email US2 / sms US3), or TOTP/backup.
        bool isValid;
        if (string.Equals(request.Method, "email", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Method, "sms", StringComparison.OrdinalIgnoreCase))
        {
            var kind = string.Equals(request.Method, "sms", StringComparison.OrdinalIgnoreCase)
                ? ChallengeMethod.SmsOtp : ChallengeMethod.EmailOtp;
            var channel = channels.Resolve(kind);
            isValid = channel is not null
                && await channel.VerifyAsync(user.PlatformUserId, OtpPurpose.Login2Fa, request.Code, cancellationToken)
                    == OtpVerifyOutcome.Verified;
        }
        else if (request.IsBackupCode)
        {
            isValid = await totpService.ValidateBackupCodeAsync(userId.Value, request.Code, cancellationToken);
        }
        else
        {
            isValid = await totpService.ValidateCodeAsync(userId.Value, request.Code, cancellationToken);
        }

        if (!isValid)
        {
            logger.LogWarning("2FA verification failed: invalid code for user {UserId}", userId.Value);
            return TypedResults.Unauthorized();
        }

        var organization = await organizationRepository.GetByIdAsync(user.OrganizationId, cancellationToken);
        if (organization is null)
        {
            return TypedResults.Unauthorized();
        }

        // Update last login timestamp
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await identityRepository.UpdateUserAsync(user, cancellationToken);

        // Generate tokens — honour a consumer-tier hint from the wallet (spec 136).
        var tokenResponse = await tokenService.GenerateUserTokenAsync(
            user, organization, user.PlatformUserId, ResolveVerify2FaTier(request.Tier), cancellationToken);

        logger.LogInformation("User completed 2FA login - UserId: {UserId}, OrgId: {OrgId}",
            user.Id, organization.Id);

        return TypedResults.Ok(tokenResponse);
    }

    /// <summary>Request to send an email one-time code mid-login.</summary>
    /// <param name="LoginToken">The short-lived login token from the login response.</param>
    public sealed record SendEmailLoginCodeRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("login_token")] string LoginToken);

    private static async Task<Results<Accepted, UnauthorizedHttpResult, StatusCodeHttpResult>> SendEmailLoginCode(
        SendEmailLoginCodeRequest request,
        ITotpService totpService,
        IIdentityRepository identityRepository,
        IVerificationChannelRegistry channels,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.LoginToken)) return TypedResults.Unauthorized();

        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null) return TypedResults.Unauthorized();

        var user = await identityRepository.GetUserByIdAsync(userId.Value, cancellationToken);
        if (user is null || user.Status != IdentityStatus.Active) return TypedResults.Unauthorized();

        var channel = channels.Resolve(ChallengeMethod.EmailOtp);
        if (channel is null) return TypedResults.Unauthorized();

        var outcome = await channel.SendAsync(user.PlatformUserId, OtpPurpose.Login2Fa, cancellationToken);
        return outcome == ChannelSendOutcome.RateLimited
            ? TypedResults.StatusCode(StatusCodes.Status429TooManyRequests)
            : TypedResults.Accepted((string?)null);
    }

    /// <summary>
    /// Resolves the mint tier for 2FA completion. Only an explicit <c>consumer</c>
    /// hint is honoured (safe downgrade); everything else keeps the platform
    /// default so the path can't escalate.
    /// </summary>
    internal static Sorcha.ServiceDefaults.Auth.Tier ResolveVerify2FaTier(string? hint)
        => string.Equals(hint, "consumer", StringComparison.OrdinalIgnoreCase)
            ? Sorcha.ServiceDefaults.Auth.Tier.Consumer
            : Sorcha.ServiceDefaults.Auth.Tier.Platform;

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> RefreshToken(
        TokenRefreshRequest request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["refreshToken"] = ["Refresh token is required"]
            });
        }

        // Spec 136 defense-in-depth: an optional tier hint lets the platform host re-evaluate the
        // minted tier against entitlement (e.g. self-heal a stale consumer token for an entitled
        // admin). Unknown/absent hint → null → tier-preserving refresh (FR-012). The entitlement
        // gate inside RefreshTokenAsync ensures this can never exceed the holder's entitlement.
        var requestedTier = RequestedTierResolver.ParseTierHint(request.Tier);
        var response = await tokenService.RefreshTokenAsync(request.RefreshToken, cancellationToken, requestedTier);

        if (response == null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<SuccessResponse>, ValidationProblem>> RevokeToken(
        TokenRevocationRequest request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["token"] = ["Token is required"]
            });
        }

        var success = await tokenService.RevokeTokenAsync(request.Token, cancellationToken);

        return TypedResults.Ok(new SuccessResponse
        {
            Success = success,
            Message = success ? "Token revoked successfully" : "Token could not be revoked"
        });
    }

    private static async Task<Ok<TokenIntrospectionResponse>> IntrospectToken(
        TokenIntrospectionRequest request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        var response = await tokenService.IntrospectTokenAsync(request.Token, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<SuccessResponse>, ValidationProblem>> RevokeUserTokens(
        RevokeUserTokensRequest request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["User ID is required"]
            });
        }

        await tokenService.RevokeAllUserTokensAsync(request.UserId, cancellationToken);

        return TypedResults.Ok(new SuccessResponse
        {
            Success = true,
            Message = $"All tokens for user {request.UserId} have been revoked"
        });
    }

    private static async Task<Results<Ok<SuccessResponse>, ValidationProblem>> RevokeOrganizationTokens(
        RevokeOrganizationTokensRequest request,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        if (request.OrganizationId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationId"] = ["Organization ID is required"]
            });
        }

        await tokenService.RevokeAllOrganizationTokensAsync(request.OrganizationId, cancellationToken);

        return TypedResults.Ok(new SuccessResponse
        {
            Success = true,
            Message = $"All tokens for organization {request.OrganizationId} have been revoked"
        });
    }

    private static Ok<CurrentUserResponse> GetCurrentUser(
        ClaimsPrincipal user)
    {
        var response = new CurrentUserResponse
        {
            UserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value,
            Email = user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value,
            DisplayName = user.FindFirst(ClaimTypes.Name)?.Value
                ?? user.FindFirst("name")?.Value,
            OrganizationId = user.FindFirst("org_id")?.Value,
            OrganizationName = user.FindFirst("org_name")?.Value,
            TokenType = user.FindFirst("token_type")?.Value ?? "user",
            Roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            Scopes = user.FindAll("scope").Select(c => c.Value).ToArray(),
            AuthMethod = user.FindFirst("auth_method")?.Value
        };

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// POST /api/auth/register — self-register a local account for public orgs.
    /// </summary>
    private static async Task<IResult> Register(
        SelfRegistrationRequest request,
        IRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        // Validate required fields
        var validationErrors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.OrgSubdomain))
            validationErrors["orgSubdomain"] = ["Organization subdomain is required"];
        if (string.IsNullOrWhiteSpace(request.Email))
            validationErrors["email"] = ["Email is required"];
        if (string.IsNullOrWhiteSpace(request.Password))
            validationErrors["password"] = ["Password is required"];
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            validationErrors["displayName"] = ["Display name is required"];

        if (validationErrors.Count > 0)
            return TypedResults.ValidationProblem(validationErrors);

        var result = await registrationService.RegisterAsync(
            request.OrgSubdomain, request.Email, request.Password,
            request.DisplayName, cancellationToken);

        if (!result.Success)
        {
            if (result.ValidationErrors is not null)
                return TypedResults.ValidationProblem(result.ValidationErrors);

            return TypedResults.Problem(
                result.Error,
                statusCode: result.ErrorStatusCode ?? StatusCodes.Status400BadRequest);
        }

        return TypedResults.Created($"/api/auth/me", new SelfRegistrationResponse
        {
            Success = true,
            UserId = result.UserId,
            Message = result.Message
        });
    }

    private static async Task<Ok<SuccessResponse>> Logout(
        HttpContext context,
        ITokenService tokenService,
        CancellationToken cancellationToken)
    {
        // Get the current access token from the Authorization header
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            await tokenService.RevokeTokenAsync(token, cancellationToken);
        }

        return TypedResults.Ok(new SuccessResponse
        {
            Success = true,
            Message = "Logged out successfully"
        });
    }

    /// <summary>
    /// POST /api/auth/verify-passkey/options — get passkey assertion options for 2FA login.
    /// </summary>
    private static async Task<IResult> VerifyPasskeyOptions(
        PasskeyAssertionOptionsRequest request,
        ITotpService totpService,
        IPasskeyService passkeyService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["login_token"] = ["Login token is required"]
            });
        }

        // Validate login token (peek — do not consume it, as we need it again for verify)
        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null)
        {
            logger.LogWarning("Passkey assertion options failed: invalid or expired login token");
            return TypedResults.Unauthorized();
        }

        // Get user's active passkey credentials
        var credentials = await passkeyService.GetCredentialsByOwnerAsync(userId.Value, cancellationToken);
        var activeCredentialIds = credentials
            .Where(c => c.Status == CredentialStatus.Active)
            .Select(c => c.CredentialId)
            .ToList();

        if (activeCredentialIds.Count == 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["passkey"] = ["No active passkey credentials found for this user"]
            });
        }

        try
        {
            var result = await passkeyService.CreateAssertionOptionsAsync(
                email: null,
                allowedCredentialIds: activeCredentialIds,
                cancellationToken: cancellationToken);

            return TypedResults.Ok(new PasskeyAssertionOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = JsonDocument.Parse(result.Options.ToJson()).RootElement
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create passkey assertion options for user {UserId}", userId.Value);
            return TypedResults.Problem("Failed to create assertion options.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/auth/verify-passkey — verify passkey assertion to complete 2FA login.
    /// </summary>
    private static async Task<IResult> VerifyPasskey(
        PasskeyVerifyRequest request,
        ITotpService totpService,
        IPasskeyService passkeyService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LoginToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["login_token"] = ["Login token is required"]
            });
        }

        // Validate login token
        var userId = await totpService.ValidateLoginTokenAsync(request.LoginToken, cancellationToken);
        if (userId is null)
        {
            logger.LogWarning("Passkey 2FA verification failed: invalid or expired login token");
            return TypedResults.Unauthorized();
        }

        try
        {
            // Verify the assertion response
            var assertionResult = await passkeyService.VerifyAssertionAsync(
                request.TransactionId,
                request.AssertionResponse,
                cancellationToken);

            // Look up user and org, then issue JWT
            var user = await identityRepository.GetUserByIdAsync(userId.Value, cancellationToken);

            // Ensure the assertion credential belongs to the login token's user
            if (user is null || assertionResult.PlatformUserId != user.PlatformUserId)
            {
                logger.LogWarning("Passkey 2FA verification failed: credential owner {PlatformUserId} does not match login user {UserId}",
                    assertionResult.PlatformUserId, userId.Value);
                return TypedResults.Unauthorized();
            }
            if (user is null || user.Status != IdentityStatus.Active)
            {
                return TypedResults.Unauthorized();
            }

            var organization = await organizationRepository.GetByIdAsync(user.OrganizationId, cancellationToken);
            if (organization is null)
            {
                return TypedResults.Unauthorized();
            }

            // Update last login timestamp
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await identityRepository.UpdateUserAsync(user, cancellationToken);

            // Generate tokens
            var tokenResponse = await tokenService.GenerateUserTokenAsync(user, organization, user.PlatformUserId, cancellationToken: cancellationToken);

            logger.LogInformation("User completed passkey 2FA login - UserId: {UserId}, OrgId: {OrgId}, CredentialId: {CredentialId}",
                user.Id, organization.Id, assertionResult.Credential.Id);

            return TypedResults.Ok(tokenResponse);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Passkey 2FA assertion verification failed for user {UserId}", userId.Value);
            return TypedResults.Unauthorized();
        }
    }

    /// <summary>
    /// POST /api/auth/create-org — self-service organisation creation.
    /// </summary>
    private static async Task<IResult> CreateOrganization(
        ProvisionOrgRequest request,
        ClaimsPrincipal principal,
        IOrgProvisioningService provisioningService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var platformUserIdClaim = principal.FindFirst("platform_user_id")?.Value;
        if (string.IsNullOrEmpty(platformUserIdClaim) || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await provisioningService.ProvisionAsync(platformUserId, request, cancellationToken);

        if (!result.Success)
        {
            if (result.ErrorCode is "EmailNotVerified" or "UserInactive" or "UserNotFound")
                return TypedResults.Json(result, statusCode: StatusCodes.Status403Forbidden);

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [result.ErrorCode ?? "error"] = [result.Error ?? "Validation failed."]
            });
        }

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// GET /api/auth/me/organizations — list the current user's org memberships.
    /// </summary>
    private static async Task<IResult> GetMyOrganizations(
        ClaimsPrincipal principal,
        TenantDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var platformUserIdClaim = principal.FindFirst("platform_user_id")?.Value;
        if (string.IsNullOrEmpty(platformUserIdClaim) || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
        {
            return TypedResults.Unauthorized();
        }

        // Current org from JWT
        var currentOrgIdClaim = principal.FindFirst("org_id")?.Value;
        Guid.TryParse(currentOrgIdClaim, out var currentOrgId);

        var memberships = await db.PlatformUserOrgMemberships
            .Where(m => m.PlatformUserId == platformUserId)
            .Join(db.Organizations,
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => new OrgMembershipEntry
                {
                    OrganizationId = o.Id,
                    OrganizationName = o.Name,
                    Subdomain = o.Subdomain,
                    Role = m.Role,
                    IsCurrent = o.Id == currentOrgId
                })
            .OrderBy(e => e.OrganizationName)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new OrgMembershipListResponse { Items = memberships });
    }

    /// <summary>
    /// POST /api/auth/switch-org — switch active organisation and issue new JWT.
    /// </summary>
    private static async Task<IResult> SwitchOrganization(
        SwitchOrgRequest request,
        ClaimsPrincipal principal,
        TenantDbContext db,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // Validate request
        if (request.OrganizationId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["organizationId"] = ["Organization ID is required."]
            });
        }

        var platformUserIdClaim = principal.FindFirst("platform_user_id")?.Value;
        if (string.IsNullOrEmpty(platformUserIdClaim) || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
        {
            return TypedResults.Unauthorized();
        }

        // Verify membership in target org
        var membership = await db.PlatformUserOrgMemberships
            .FirstOrDefaultAsync(m => m.PlatformUserId == platformUserId
                && m.OrganizationId == request.OrganizationId, cancellationToken);

        if (membership is null)
        {
            return TypedResults.Json(
                new { error = "You are not a member of the target organisation." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Look up the target org
        var targetOrg = await db.Organizations
            .FirstOrDefaultAsync(o => o.Id == request.OrganizationId, cancellationToken);

        if (targetOrg is null || targetOrg.Status != OrganizationStatus.Active)
        {
            return TypedResults.Json(
                new { error = "Target organisation is not available." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Find the user's UserIdentity in the target org
        var userIdentity = await db.UserIdentities
            .FirstOrDefaultAsync(ui => ui.PlatformUserId == platformUserId
                && ui.OrganizationId == request.OrganizationId
                && ui.Status == IdentityStatus.Active, cancellationToken);

        if (userIdentity is null)
        {
            return TypedResults.Json(
                new { error = "No active identity found in the target organisation." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Spec 136 (FR-016): re-mint at the tier appropriate to the NEW context — Platform if the
        // user holds a platform role in the target org, otherwise Consumer (the tier follows the
        // person in that context). Switching context is destination-derived, never an explicit
        // over-request, so it downgrades rather than refuses.
        var contextTier = TierResolver.ResolvePreference(
            Sorcha.ServiceDefaults.Auth.Tier.Platform, isExplicit: false, userIdentity.Roles).Tier;

        // Issue new JWT scoped to target org
        var tokenResponse = await tokenService.GenerateUserTokenAsync(
            userIdentity, targetOrg, platformUserId, contextTier, cancellationToken);

        logger.LogInformation(
            "User {PlatformUserId} switched to org {OrgId} ({Subdomain}) at tier {Tier}",
            platformUserId, targetOrg.Id, targetOrg.Subdomain, contextTier);

        return TypedResults.Ok(tokenResponse);
    }
}
