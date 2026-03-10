// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
            .Produces<PasskeyAssertionOptionsResponse>()
            .ProducesValidationProblem();

        assertionGroup.MapPost("/assertion/verify", AssertionVerify)
            .WithName("PasskeyAssertionVerify")
            .WithSummary("Verify passkey assertion and complete sign-in")
            .WithDescription("Verifies the assertion response from the authenticator and issues a JWT. "
                + "Works for both public users and organization users — the owner type is determined "
                + "from the credential's registration data.")
            .AllowAnonymous()
            .Produces<PublicTokenResponse>()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

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
                "PublicIdentity",
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
                        "PublicIdentity", publicUser.Id, cancellationToken);
                    allowedCredentialIds.AddRange(publicCredentials
                        .Where(c => c.Status == CredentialStatus.Active)
                        .Select(c => c.CredentialId));
                }

                // Check org users
                var orgUser = await identityRepository.GetUserByEmailAsync(request.Email, cancellationToken);
                if (orgUser is not null)
                {
                    var orgCredentials = await passkeyService.GetCredentialsByOwnerAsync(
                        "OrgUser", orgUser.Id, cancellationToken);
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

            if (assertionResult.OwnerType == "PublicIdentity")
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

                if (publicUser.Status != "Active")
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
            else if (assertionResult.OwnerType == "OrgUser")
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
}
