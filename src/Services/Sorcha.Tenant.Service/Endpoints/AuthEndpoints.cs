// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;

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
            .Produces(StatusCodes.Status401Unauthorized);

        // Verify 2FA code after login (public endpoint — uses loginToken)
        group.MapPost("/verify-2fa", Verify2Fa)
            .WithName("Verify2Fa")
            .WithSummary("Verify TOTP 2FA code to complete login")
            .WithDescription("Accepts a loginToken (from login response) and a TOTP code or backup code. "
                + "Returns access/refresh tokens on successful verification.")
            .AllowAnonymous()
            .RequireRateLimiting(TotpEndpoints.TotpRateLimitPolicy)
            .Produces<TokenResponse>()
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
            .Produces(StatusCodes.Status401Unauthorized);

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
            .RequireAuthorization("RequireAdministrator")
            .Produces<SuccessResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Revoke all organization tokens (admin only)
        group.MapPost("/token/revoke-organization", RevokeOrganizationTokens)
            .WithName("RevokeOrganizationTokens")
            .WithSummary("Revoke all tokens for an organization")
            .WithDescription("Revokes all access and refresh tokens for all users in an organization. Administrator only.")
            .RequireAuthorization("RequireAdministrator")
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
            .Produces(StatusCodes.Status401Unauthorized);

        // Self-registration with email/password (public endpoint)
        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Self-register with email/password")
            .WithDescription("Creates a local account for public organizations with self-registration enabled. "
                + "Validates password against NIST policy and HIBP breach list. Sends verification email.")
            .AllowAnonymous()
            .Produces<SelfRegistrationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

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

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        ILoginService loginService,
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

        var result = await loginService.LoginAsync(request.Email, request.Password, cancellationToken);

        if (!result.Success)
        {
            // Rate-limited responses get 429
            if (result.ErrorCode == LoginErrorCode.RateLimited)
            {
                return TypedResults.Problem(result.Error,
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return TypedResults.Unauthorized();
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

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult, ValidationProblem>> Verify2Fa(
        Verify2FaRequest request,
        ITotpService totpService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
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

        // Validate the code (TOTP or backup)
        bool isValid;
        if (request.IsBackupCode)
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

        // Code valid — look up user and org, then issue JWT
        var user = await identityRepository.GetUserByIdAsync(userId.Value, cancellationToken);
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
        var tokenResponse = await tokenService.GenerateUserTokenAsync(user, organization, cancellationToken);

        logger.LogInformation("User completed 2FA login - UserId: {UserId}, OrgId: {OrgId}",
            user.Id, organization.Id);

        return TypedResults.Ok(tokenResponse);
    }

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

        var response = await tokenService.RefreshTokenAsync(request.RefreshToken, cancellationToken);

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
        var credentials = await passkeyService.GetCredentialsByOwnerAsync(OwnerTypes.OrgUser, userId.Value, cancellationToken);
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
                Options = result.Options
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

            // Ensure the assertion credential belongs to the login token's user
            if (assertionResult.OwnerId != userId.Value)
            {
                logger.LogWarning("Passkey 2FA verification failed: credential owner {OwnerId} does not match login user {UserId}",
                    assertionResult.OwnerId, userId.Value);
                return TypedResults.Unauthorized();
            }

            // Look up user and org, then issue JWT
            var user = await identityRepository.GetUserByIdAsync(userId.Value, cancellationToken);
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
            var tokenResponse = await tokenService.GenerateUserTokenAsync(user, organization, cancellationToken);

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
}
