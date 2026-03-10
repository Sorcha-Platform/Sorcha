// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Public passkey authentication endpoints for user signup and sign-in.
/// These endpoints do not require prior authentication — they are the entry points
/// for new public users (passkey registration) and returning users (passkey assertion).
/// </summary>
public static class PublicAuthEndpoints
{
    /// <summary>
    /// Rate limiter policy name for public authentication endpoints.
    /// Limits to 5 attempts per minute per IP to prevent brute-force attacks.
    /// </summary>
    internal const string PublicAuthRateLimitPolicy = "public-auth";

    /// <summary>
    /// Maps public authentication endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPublicAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Public User Signup (Passkey Registration) ---
        var registerGroup = app.MapGroup("/api/auth/public/passkey")
            .WithTags("Public Authentication");

        registerGroup.MapPost("/register/options", RegisterOptions)
            .WithName("PublicPasskeyRegisterOptions")
            .WithSummary("Generate passkey registration options for public user signup")
            .WithDescription("Creates FIDO2 credential creation options for a new public user. "
                + "If an email is provided and already in use, returns 409 Conflict. "
                + "Returns a transaction ID and challenge options to pass to the browser WebAuthn API.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<PasskeyRegistrationOptionsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        registerGroup.MapPost("/register/verify", RegisterVerify)
            .WithName("PublicPasskeyRegisterVerify")
            .WithSummary("Verify passkey attestation and complete public user signup")
            .WithDescription("Verifies the attestation response from the authenticator, creates a new public user "
                + "with the registered passkey credential, and issues a JWT. "
                + "The transaction ID must match a pending registration challenge.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<PublicTokenResponse>()
            .ProducesValidationProblem();

        // --- Passkey Assertion (Sign-in for all user types) ---
        var assertionGroup = app.MapGroup("/api/auth/passkey")
            .WithTags("Passkey Authentication");

        assertionGroup.MapPost("/assertion/options", AssertionOptions)
            .WithName("PasskeyAssertionOptions")
            .WithSummary("Generate passkey assertion options for sign-in")
            .WithDescription("Creates FIDO2 assertion options for passkey sign-in. "
                + "Optionally accepts an email to narrow allowed credentials for non-discoverable flow. "
                + "Returns a transaction ID and challenge options to pass to the browser WebAuthn API.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<PasskeyAssertionOptionsResponse>()
            .ProducesValidationProblem();

        assertionGroup.MapPost("/assertion/verify", AssertionVerify)
            .WithName("PasskeyAssertionVerify")
            .WithSummary("Verify passkey assertion and complete sign-in")
            .WithDescription("Verifies the assertion response from the authenticator and issues a JWT. "
                + "Works for both public users and organization users — the owner type is determined "
                + "from the credential's registration data.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<PublicTokenResponse>()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // --- Social Login ---
        var socialGroup = app.MapGroup("/api/auth/public/social")
            .WithTags("Public Authentication");

        socialGroup.MapPost("/initiate", SocialInitiate)
            .WithName("SocialLoginInitiate")
            .WithSummary("Initiate social login authorization flow")
            .WithDescription("Generates an authorization URL for the specified social provider (Google, Microsoft, GitHub, Apple). "
                + "The client should redirect the user to the returned URL. "
                + "Uses PKCE and state parameter for security.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<SocialInitiateResponse>()
            .ProducesValidationProblem();

        socialGroup.MapPost("/callback", SocialCallback)
            .WithName("SocialLoginCallback")
            .WithSummary("Complete social login with authorization code")
            .WithDescription("Exchanges the authorization code from the social provider for user claims, "
                + "creates or links a public user account, and issues a JWT. "
                + "Returns isNewUser=true for new accounts, false for existing accounts with newly linked social login.")
            .AllowAnonymous()
            .RequireRateLimiting(PublicAuthRateLimitPolicy)
            .Produces<PublicTokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        // --- Authenticated Public User Management ---
        var managementGroup = app.MapGroup("/api/auth/public")
            .WithTags("Public User Management")
            .RequireAuthorization();

        managementGroup.MapGet("/methods", GetAuthMethods)
            .WithName("GetAuthMethods")
            .WithSummary("List all authentication methods for the current public user")
            .WithDescription("Returns all passkey credentials and social login links associated with the "
                + "authenticated public user. Requires a valid public user JWT.")
            .Produces<AuthMethodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        managementGroup.MapPost("/social/link", LinkSocialAccount)
            .WithName("LinkSocialAccount")
            .WithSummary("Initiate social account linking for an authenticated user")
            .WithDescription("Generates an authorization URL for the specified social provider. "
                + "The user should be redirected to this URL to complete the linking flow. "
                + "Unlike the anonymous social initiate endpoint, this links to an existing account.")
            .Produces<SocialInitiateResponse>()
            .ProducesValidationProblem();

        managementGroup.MapDelete("/social/{linkId:guid}", UnlinkSocialAccount)
            .WithName("UnlinkSocialAccount")
            .WithSummary("Remove a social login link from the current public user")
            .WithDescription("Removes the specified social login link. Returns 400 Bad Request if "
                + "this is the user's last authentication method (last-method guard prevents account lockout).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        managementGroup.MapPost("/passkey/add/options", AddPasskeyOptions)
            .WithName("PublicAddPasskeyOptions")
            .WithSummary("Generate passkey registration options for an authenticated public user")
            .WithDescription("Creates FIDO2 credential creation options for adding an additional passkey "
                + "to an existing public user account. Excludes already-registered credential IDs. "
                + "Returns a transaction ID and challenge options to pass to the browser WebAuthn API.")
            .Produces<PasskeyRegistrationOptionsResponse>()
            .ProducesValidationProblem();

        managementGroup.MapPost("/passkey/add/verify", AddPasskeyVerify)
            .WithName("PublicAddPasskeyVerify")
            .WithSummary("Verify passkey attestation and add credential to authenticated public user")
            .WithDescription("Verifies the attestation response from the authenticator and links the new "
                + "passkey credential to the authenticated public user's account. "
                + "The transaction ID must match a pending registration challenge.")
            .Produces<PasskeyCredentialResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return app;
    }

    /// <summary>
    /// POST /api/auth/public/passkey/register/options — generate registration challenge for new public user.
    /// </summary>
    private static readonly TimeSpan EmailCacheTtl = TimeSpan.FromMinutes(5);

    private static async Task<IResult> RegisterOptions(
        PublicPasskeyRegisterOptionsRequest request,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        IDistributedCache cache,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name is required"]
            });
        }

        // Check for duplicate email before generating challenge
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existingUser = await publicUserService.GetPublicUserByEmailAsync(request.Email, cancellationToken);
            if (existingUser is not null)
            {
                return TypedResults.Conflict(new { error = "A user with this email address already exists." });
            }
        }

        try
        {
            // Generate a temporary user ID for the registration ceremony
            // This will become the PublicIdentity.Id once registration is verified
            var tempUserId = Guid.NewGuid();

            var result = await passkeyService.CreateRegistrationOptionsAsync(
                OwnerTypes.PublicIdentity,
                tempUserId,
                organizationId: null,
                request.DisplayName,
                existingCredentialIds: null,
                cancellationToken);

            // Store the email alongside the challenge so it can be retrieved during verification.
            // The PasskeyService cache stores credential data; this is supplementary user metadata.
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var emailCacheKey = $"passkey:email:{result.TransactionId}";
                await cache.SetAsync(
                    emailCacheKey,
                    Encoding.UTF8.GetBytes(request.Email),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = EmailCacheTtl },
                    cancellationToken);
            }

            logger.LogInformation(
                "Public passkey registration options created for displayName={DisplayName}, email={Email}",
                request.DisplayName, request.Email ?? "(none)");

            return TypedResults.Ok(new PasskeyRegistrationOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = result.Options
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create public passkey registration options");
            return TypedResults.Problem(
                "Failed to create registration options.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/auth/public/passkey/register/verify — verify attestation, create user, issue token.
    /// </summary>
    private static async Task<IResult> RegisterVerify(
        PublicPasskeyRegisterVerifyRequest request,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        ITokenService tokenService,
        IDistributedCache cache,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction_id"] = ["Transaction ID is required"]
            });
        }

        try
        {
            // Verify the attestation response and get the credential
            var credential = await passkeyService.VerifyRegistrationAsync(
                request.TransactionId,
                request.AttestationResponse,
                cancellationToken);

            // Retrieve the email stored during the options step
            string? email = null;
            var emailCacheKey = $"passkey:email:{request.TransactionId}";
            var emailBytes = await cache.GetAsync(emailCacheKey, cancellationToken);
            if (emailBytes is not null)
            {
                email = Encoding.UTF8.GetString(emailBytes);
                await cache.RemoveAsync(emailCacheKey, cancellationToken);
            }

            var userResult = await publicUserService.CreatePublicUserAsync(
                credential.DisplayName,
                email,
                credential,
                cancellationToken);

            if (!userResult.Success)
            {
                logger.LogWarning("Public user creation failed: {Reason}", userResult.ConflictReason);
                return TypedResults.Conflict(new { error = userResult.ConflictReason });
            }

            // Issue JWT for the new public user
            var tokenResponse = await tokenService.GeneratePublicUserTokenAsync(
                userResult.Identity!,
                cancellationToken);

            logger.LogInformation(
                "Public user registered and signed in: {UserId}",
                userResult.Identity!.Id);

            return TypedResults.Json(new PublicTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope,
                IsNewUser = true
            }, statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Public passkey registration verification failed");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["attestation_response"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// POST /api/auth/passkey/assertion/options — generate assertion challenge for sign-in.
    /// </summary>
    private static async Task<IResult> AssertionOptions(
        PublicPasskeyAssertionOptionsRequest request,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var allowedCredentialIds = new List<byte[]>();

            // If email is provided, look up credentials for both public and org users
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                // Check public users
                var publicUser = await publicUserService.GetPublicUserByEmailAsync(request.Email, cancellationToken);
                if (publicUser is not null)
                {
                    var publicCredentials = await passkeyService.GetCredentialsByOwnerAsync(
                        OwnerTypes.PublicIdentity, publicUser.Id, cancellationToken);
                    allowedCredentialIds.AddRange(publicCredentials
                        .Where(c => c.Status == CredentialStatus.Active)
                        .Select(c => c.CredentialId));
                }

                // Check org users
                var orgUser = await identityRepository.GetUserByEmailAsync(request.Email, cancellationToken);
                if (orgUser is not null)
                {
                    var orgCredentials = await passkeyService.GetCredentialsByOwnerAsync(
                        OwnerTypes.OrgUser, orgUser.Id, cancellationToken);
                    allowedCredentialIds.AddRange(orgCredentials
                        .Where(c => c.Status == CredentialStatus.Active)
                        .Select(c => c.CredentialId));
                }

                // If neither found, still proceed — the authenticator may use discoverable credentials
            }

            var result = await passkeyService.CreateAssertionOptionsAsync(
                request.Email,
                allowedCredentialIds.Count > 0 ? allowedCredentialIds : null,
                cancellationToken);

            logger.LogInformation(
                "Passkey assertion options created, email={Email}",
                request.Email ?? "(discoverable)");

            return TypedResults.Ok(new PasskeyAssertionOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = result.Options
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create passkey assertion options");
            return TypedResults.Problem(
                "Failed to create assertion options.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/auth/passkey/assertion/verify — verify assertion and issue token.
    /// Handles both PublicIdentity and OrgUser credential owners.
    /// </summary>
    private static async Task<IResult> AssertionVerify(
        PublicPasskeyAssertionVerifyRequest request,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction_id"] = ["Transaction ID is required"]
            });
        }

        try
        {
            var assertionResult = await passkeyService.VerifyAssertionAsync(
                request.TransactionId,
                request.AssertionResponse,
                cancellationToken);

            if (assertionResult.OwnerType == OwnerTypes.PublicIdentity)
            {
                // Public user sign-in
                var publicUser = await publicUserService.GetPublicUserByIdAsync(
                    assertionResult.OwnerId, cancellationToken);

                if (publicUser is null)
                {
                    logger.LogWarning(
                        "Passkey assertion verified but public user not found: {OwnerId}",
                        assertionResult.OwnerId);
                    return TypedResults.Unauthorized();
                }

                if (publicUser.Status != nameof(IdentityStatus.Active))
                {
                    logger.LogWarning(
                        "Passkey assertion for suspended/deleted public user: {UserId}",
                        publicUser.Id);
                    return TypedResults.Unauthorized();
                }

                var publicToken = await tokenService.GeneratePublicUserTokenAsync(
                    publicUser, cancellationToken);

                logger.LogInformation(
                    "Public user signed in via passkey: {UserId}", publicUser.Id);

                return TypedResults.Ok(new PublicTokenResponse
                {
                    AccessToken = publicToken.AccessToken,
                    RefreshToken = publicToken.RefreshToken,
                    TokenType = publicToken.TokenType,
                    ExpiresIn = publicToken.ExpiresIn,
                    Scope = publicToken.Scope,
                    IsNewUser = false
                });
            }
            else if (assertionResult.OwnerType == OwnerTypes.OrgUser)
            {
                // Organization user sign-in
                var orgUser = await identityRepository.GetUserByIdAsync(
                    assertionResult.OwnerId, cancellationToken);

                if (orgUser is null)
                {
                    logger.LogWarning(
                        "Passkey assertion verified but org user not found: {OwnerId}",
                        assertionResult.OwnerId);
                    return TypedResults.Unauthorized();
                }

                if (orgUser.Status != Models.IdentityStatus.Active)
                {
                    logger.LogWarning(
                        "Passkey assertion for inactive org user: {UserId}",
                        orgUser.Id);
                    return TypedResults.Unauthorized();
                }

                var organization = await organizationRepository.GetByIdAsync(
                    orgUser.OrganizationId, cancellationToken);

                if (organization is null)
                {
                    logger.LogError(
                        "Passkey assertion: organization not found for org user {UserId}, orgId={OrgId}",
                        orgUser.Id, orgUser.OrganizationId);
                    return TypedResults.Unauthorized();
                }

                var orgToken = await tokenService.GenerateUserTokenAsync(
                    orgUser, organization, cancellationToken);

                logger.LogInformation(
                    "Org user signed in via passkey: {UserId}, org={OrgId}",
                    orgUser.Id, orgUser.OrganizationId);

                return TypedResults.Ok(orgToken);
            }
            else
            {
                logger.LogError(
                    "Passkey assertion: unknown owner type {OwnerType} for owner {OwnerId}",
                    assertionResult.OwnerType, assertionResult.OwnerId);
                return TypedResults.Unauthorized();
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Passkey assertion verification failed");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["assertion_response"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// Validates that a redirect URI is on the configured allowlist.
    /// Prevents open-redirect attacks by ensuring OAuth callbacks only go to trusted domains.
    /// </summary>
    private static bool IsRedirectUriAllowed(string redirectUri, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("SocialLogin:AllowedRedirectOrigins").Get<string[]>();

        // If no allowlist configured, only allow same-origin (relative paths)
        if (allowedOrigins is null || allowedOrigins.Length == 0)
        {
            return redirectUri.StartsWith('/');
        }

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
        {
            return redirectUri.StartsWith('/'); // Allow relative paths
        }

        var origin = $"{uri.Scheme}://{uri.Authority}";
        return allowedOrigins.Any(allowed =>
            string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// POST /api/auth/public/social/initiate — generate social login authorization URL.
    /// </summary>
    private static async Task<IResult> SocialInitiate(
        SocialInitiateRequest request,
        ISocialLoginService socialLoginService,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = ["Provider is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["redirect_uri"] = ["Redirect URI is required"]
            });
        }

        if (!IsRedirectUriAllowed(request.RedirectUri, configuration))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["redirect_uri"] = ["Redirect URI is not on the allowed origins list"]
            });
        }

        try
        {
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, request.RedirectUri, cancellationToken);

            logger.LogInformation("Social login initiated for provider {Provider}", request.Provider);

            return TypedResults.Ok(new SocialInitiateResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social login initiate failed: provider not configured");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// POST /api/auth/public/social/callback — exchange code, create/link user, issue token.
    /// </summary>
    private static async Task<IResult> SocialCallback(
        SocialCallbackRequest request,
        ISocialLoginService socialLoginService,
        IPublicUserService publicUserService,
        ITokenService tokenService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = ["Provider is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["Authorization code is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["state"] = ["State parameter is required"]
            });
        }

        try
        {
            // Exchange the authorization code for user claims
            var authResult = await socialLoginService.ExchangeCodeAsync(
                request.Provider, request.Code, request.State, cancellationToken);

            if (!authResult.Success)
            {
                logger.LogWarning("Social login callback failed for provider {Provider}: {Error}",
                    request.Provider, authResult.Error);
                return TypedResults.Unauthorized();
            }

            if (string.IsNullOrEmpty(authResult.Subject))
            {
                logger.LogWarning("Social login returned no subject for provider {Provider}", request.Provider);
                return TypedResults.Unauthorized();
            }

            // Create or link the social login to a public user
            var socialLoginLink = new SocialLoginLink
            {
                ProviderType = authResult.Provider,
                ExternalSubjectId = authResult.Subject,
                LinkedEmail = authResult.Email,
                DisplayName = authResult.DisplayName,
                LastUsedAt = DateTimeOffset.UtcNow
            };

            var userResult = await publicUserService.CreatePublicUserFromSocialAsync(
                authResult.DisplayName ?? authResult.Email ?? "Social User",
                authResult.Email,
                socialLoginLink,
                cancellationToken);

            if (!userResult.Success)
            {
                logger.LogWarning("Social login user creation/linking failed: {Reason}", userResult.ConflictReason);
                return TypedResults.Unauthorized();
            }

            // Issue JWT for the public user
            var tokenResponse = await tokenService.GeneratePublicUserTokenAsync(
                userResult.Identity!,
                cancellationToken);

            logger.LogInformation(
                "Social login completed for provider {Provider}: userId={UserId}, isNewUser={IsNewUser}",
                request.Provider, userResult.Identity!.Id, userResult.IsNewUser);

            return TypedResults.Ok(new PublicTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope,
                IsNewUser = userResult.IsNewUser
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social login callback failed: provider not configured");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// Extracts the public user ID from JWT claims. Returns null if the claim is missing or invalid.
    /// </summary>
    private static Guid? GetPublicUserId(HttpContext context)
    {
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? context.User.FindFirst("sub")?.Value;

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    /// <summary>
    /// GET /api/auth/public/methods — list all auth methods for the authenticated public user.
    /// </summary>
    private static async Task<IResult> GetAuthMethods(
        HttpContext context,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userId = GetPublicUserId(context);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var identity = await publicUserService.GetPublicUserByIdAsync(userId.Value, cancellationToken);
        if (identity is null)
        {
            return TypedResults.Unauthorized();
        }

        var credentials = await passkeyService.GetCredentialsByOwnerAsync(
            OwnerTypes.PublicIdentity, userId.Value, cancellationToken);

        var passkeys = credentials
            .Where(c => c.Status == CredentialStatus.Active)
            .Select(c => new AuthMethodPasskeyItem
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                DeviceType = c.DeviceType,
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                LastUsedAt = c.LastUsedAt
            })
            .ToList();

        var socialLinks = identity.SocialLoginLinks
            .Select(s => new AuthMethodSocialLinkItem
            {
                Id = s.Id,
                Provider = s.ProviderType,
                Email = s.LinkedEmail,
                DisplayName = s.DisplayName,
                CreatedAt = s.CreatedAt,
                LastUsedAt = s.LastUsedAt
            })
            .ToList();

        logger.LogInformation(
            "Auth methods listed for user {UserId}: {PasskeyCount} passkeys, {SocialCount} social links",
            userId.Value, passkeys.Count, socialLinks.Count);

        return TypedResults.Ok(new AuthMethodsResponse
        {
            Passkeys = passkeys,
            SocialLinks = socialLinks
        });
    }

    /// <summary>
    /// POST /api/auth/public/social/link — initiate social account linking for authenticated user.
    /// </summary>
    private static async Task<IResult> LinkSocialAccount(
        SocialInitiateRequest request,
        HttpContext context,
        ISocialLoginService socialLoginService,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userId = GetPublicUserId(context);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = ["Provider is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["redirect_uri"] = ["Redirect URI is required"]
            });
        }

        if (!IsRedirectUriAllowed(request.RedirectUri, configuration))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["redirect_uri"] = ["Redirect URI is not on the allowed origins list"]
            });
        }

        try
        {
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, request.RedirectUri, cancellationToken);

            logger.LogInformation(
                "Social account linking initiated for user {UserId}, provider {Provider}",
                userId.Value, request.Provider);

            return TypedResults.Ok(new SocialInitiateResponse
            {
                AuthorizationUrl = result.AuthorizationUrl,
                State = result.State
            });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Social link initiate failed: provider not configured");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["provider"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// DELETE /api/auth/public/social/{linkId} — remove a social login link.
    /// </summary>
    private static async Task<IResult> UnlinkSocialAccount(
        Guid linkId,
        HttpContext context,
        IPublicUserService publicUserService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userId = GetPublicUserId(context);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await publicUserService.RemoveSocialLinkAsync(userId.Value, linkId, cancellationToken);

        if (result.IsLastMethodGuard)
        {
            return TypedResults.Problem(
                result.Error,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!result.Success)
        {
            return TypedResults.NotFound();
        }

        logger.LogInformation(
            "Social login link {LinkId} removed for user {UserId}",
            linkId, userId.Value);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// POST /api/auth/public/passkey/add/options — generate registration options for adding a passkey.
    /// </summary>
    private static async Task<IResult> AddPasskeyOptions(
        PublicPasskeyAddOptionsRequest request,
        HttpContext context,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userId = GetPublicUserId(context);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name is required"]
            });
        }

        var identity = await publicUserService.GetPublicUserByIdAsync(userId.Value, cancellationToken);
        if (identity is null)
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            // Get existing credential IDs to exclude from re-registration
            var existingCredentials = await passkeyService.GetCredentialsByOwnerAsync(
                OwnerTypes.PublicIdentity, userId.Value, cancellationToken);
            var existingCredentialIds = existingCredentials
                .Where(c => c.Status == CredentialStatus.Active)
                .Select(c => c.CredentialId)
                .ToList();

            var result = await passkeyService.CreateRegistrationOptionsAsync(
                OwnerTypes.PublicIdentity,
                userId.Value,
                organizationId: null,
                request.DisplayName,
                existingCredentialIds,
                cancellationToken);

            logger.LogInformation(
                "Passkey add options created for user {UserId}, displayName={DisplayName}",
                userId.Value, request.DisplayName);

            return TypedResults.Ok(new PasskeyRegistrationOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = result.Options
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create passkey add options for user {UserId}", userId.Value);
            return TypedResults.Problem(
                "Failed to create registration options.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/auth/public/passkey/add/verify — verify attestation and add passkey to authenticated user.
    /// </summary>
    private static async Task<IResult> AddPasskeyVerify(
        PublicPasskeyAddVerifyRequest request,
        HttpContext context,
        IPasskeyService passkeyService,
        IPublicUserService publicUserService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userId = GetPublicUserId(context);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction_id"] = ["Transaction ID is required"]
            });
        }

        var identity = await publicUserService.GetPublicUserByIdAsync(userId.Value, cancellationToken);
        if (identity is null)
        {
            return TypedResults.Unauthorized();
        }

        try
        {
            var credential = await passkeyService.VerifyRegistrationAsync(
                request.TransactionId,
                request.AttestationResponse,
                cancellationToken);

            logger.LogInformation(
                "Passkey added for user {UserId}, credentialId={CredentialId}",
                userId.Value, credential.Id);

            return TypedResults.Json(new PasskeyCredentialResponse
            {
                Id = credential.Id,
                DisplayName = credential.DisplayName,
                DeviceType = credential.DeviceType,
                Status = credential.Status.ToString(),
                CreatedAt = credential.CreatedAt,
                LastUsedAt = credential.LastUsedAt
            }, statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Passkey add verification failed for user {UserId}", userId.Value);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["attestation_response"] = [ex.Message]
            });
        }
    }
}
