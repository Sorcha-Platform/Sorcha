// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Public passkey registration and passwordless sign-in endpoints.
/// These endpoints are anonymous and used for new user signup and returning user authentication.
/// </summary>
public static class PublicPasskeyEndpoints
{
    /// <summary>
    /// Maps public passkey endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPublicPasskeyEndpoints(this IEndpointRouteBuilder app)
    {
        var registerGroup = app.MapGroup("/api/auth/public/passkey/register")
            .WithTags("Public Passkey Registration");

        registerGroup.MapPost("/options", RegisterOptions)
            .WithName("PublicPasskeyRegisterOptions")
            .WithSummary("Generate passkey registration options for new user signup")
            .WithDescription("Creates a new PlatformUser and generates FIDO2 credential creation options. "
                + "The public organisation must be enabled. If an email is provided, it must be unique.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<PasskeyRegistrationOptionsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        registerGroup.MapPost("/verify", RegisterVerify)
            .WithName("PublicPasskeyRegisterVerify")
            .WithSummary("Verify passkey registration and complete signup")
            .WithDescription("Verifies the attestation response from the authenticator, creates a UserIdentity "
                + "in the public organisation, and issues a JWT access token.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<PublicTokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest);

        var assertionGroup = app.MapGroup("/api/auth/passkey/assertion")
            .WithTags("Public Passkey Authentication");

        assertionGroup.MapPost("/options", AssertionOptions)
            .WithName("PublicPasskeyAssertionOptions")
            .WithSummary("Generate passkey assertion options for sign-in")
            .WithDescription("Creates FIDO2 assertion options for passwordless sign-in. "
                + "Optionally accepts an email to narrow down allowed credentials.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<PasskeyAssertionOptionsResponse>()
            .ProducesValidationProblem();

        assertionGroup.MapPost("/verify", AssertionVerify)
            .WithName("PublicPasskeyAssertionVerify")
            .WithSummary("Verify passkey assertion and complete sign-in")
            .WithDescription("Verifies the assertion response from the authenticator, resolves the user, "
                + "and issues a JWT access token.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<PublicTokenResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>
    /// POST /api/auth/public/passkey/register/options — generate registration options for new user signup.
    /// </summary>
    private static async Task<IResult> RegisterOptions(
        PublicPasskeyRegisterOptionsRequest request,
        IPasskeyService passkeyService,
        IPlatformUserService platformUserService,
        IPlatformSettingsService platformSettingsService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate display_name
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name is required"]
            });
        }

        // Check public org is enabled
        var settings = await platformSettingsService.GetAsync(ct);
        if (!settings.PublicOrgEnabled)
        {
            return TypedResults.Problem(
                "Public organisation is not enabled. Passkey registration is unavailable.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // If email provided, check uniqueness
        string email;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existing = await platformUserService.GetByEmailAsync(request.Email, ct);
            if (existing is not null)
            {
                return TypedResults.Conflict(new { error = "A user with this email address already exists." });
            }
            email = request.Email;
        }
        else
        {
            // Placeholder email for passkey-only users
            email = $"passkey-{Guid.NewGuid():N}@placeholder.local";
        }

        try
        {
            // Create PlatformUser (WebAuthn requires user.id for credential creation)
            var platformUser = await platformUserService.CreateAsync(
                email, request.DisplayName, passwordHash: null, ct);

            var result = await passkeyService.CreateRegistrationOptionsAsync(
                platformUser.Id, request.DisplayName, cancellationToken: ct);

            logger.LogInformation(
                "Public passkey registration options created for new PlatformUser {PlatformUserId}",
                platformUser.Id);

            return TypedResults.Ok(new PasskeyRegistrationOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = JsonDocument.Parse(result.Options.ToJson()).RootElement
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
    /// POST /api/auth/public/passkey/register/verify — verify registration and complete signup.
    /// </summary>
    private static async Task<IResult> RegisterVerify(
        PublicPasskeyRegisterVerifyRequest request,
        IPasskeyService passkeyService,
        IPlatformUserService platformUserService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        TenantDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate transaction_id
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction_id"] = ["Transaction ID is required"]
            });
        }

        try
        {
            // Verify registration — this returns the credential with PlatformUserId
            var credential = await passkeyService.VerifyRegistrationAsync(
                request.TransactionId, request.AttestationResponse, persist: true, ct);

            // Look up the PlatformUser
            var platformUser = await platformUserService.GetByIdAsync(credential.PlatformUserId, ct);
            if (platformUser is null)
            {
                return TypedResults.Problem(
                    "Platform user not found.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Create UserIdentity in the public org
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
                    ProvisionedVia = ProvisioningMethod.Passkey,
                    ProfileCompleted = !string.IsNullOrWhiteSpace(platformUser.Email)
                        && !platformUser.Email.EndsWith("@placeholder.local")
                        && !string.IsNullOrWhiteSpace(platformUser.DisplayName)
                };

                await identityRepository.CreateUserAsync(userIdentity, ct);

                logger.LogInformation(
                    "Created UserIdentity {UserIdentityId} in public org for PlatformUser {PlatformUserId} via passkey",
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
                "Public passkey registration completed for PlatformUser {PlatformUserId}",
                platformUser.Id);

            return TypedResults.Ok(new PublicTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope,
                IsNewUser = true
            });
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
    /// POST /api/auth/passkey/assertion/options — generate assertion options for sign-in.
    /// </summary>
    private static async Task<IResult> AssertionOptions(
        PublicPasskeyAssertionOptionsRequest request,
        IPasskeyService passkeyService,
        IPlatformUserService platformUserService,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        IEnumerable<byte[]>? allowedCredentialIds = null;

        // If email provided, look up user's active credential IDs
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var platformUser = await platformUserService.GetByEmailAsync(request.Email, ct);
            if (platformUser is not null)
            {
                var credentials = await passkeyService.GetCredentialsByOwnerAsync(platformUser.Id, ct);
                allowedCredentialIds = credentials
                    .Where(c => c.Status == CredentialStatus.Active)
                    .Select(c => c.CredentialId)
                    .ToList();
            }
            // If user not found, still proceed — discoverable credentials may work
        }

        try
        {
            var result = await passkeyService.CreateAssertionOptionsAsync(
                request.Email, allowedCredentialIds, ct);

            logger.LogInformation("Public passkey assertion options created");

            return TypedResults.Ok(new PasskeyAssertionOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = JsonDocument.Parse(result.Options.ToJson()).RootElement
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
    /// POST /api/auth/passkey/assertion/verify — verify assertion and complete sign-in.
    /// </summary>
    private static async Task<IResult> AssertionVerify(
        PublicPasskeyAssertionVerifyRequest request,
        IPasskeyService passkeyService,
        IPlatformUserService platformUserService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        TenantDbContext db,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate transaction_id
        if (string.IsNullOrWhiteSpace(request.TransactionId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["transaction_id"] = ["Transaction ID is required"]
            });
        }

        try
        {
            // Verify assertion — returns credential + platform user ID
            var assertionResult = await passkeyService.VerifyAssertionAsync(
                request.TransactionId, request.AssertionResponse, ct);

            // Look up PlatformUser
            var platformUser = await platformUserService.GetByIdAsync(assertionResult.PlatformUserId, ct);
            if (platformUser is null || platformUser.Status != PlatformUserStatus.Active)
            {
                return TypedResults.Problem(
                    "User account is not active.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            // Look up UserIdentity in public org
            var publicOrgId = WellKnownIds.PublicOrgId;
            var userIdentity = await db.UserIdentities
                .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.OrganizationId == publicOrgId, ct);

            if (userIdentity is null || userIdentity.Status != IdentityStatus.Active)
            {
                return TypedResults.Problem(
                    "User identity not found in public organisation.",
                    statusCode: StatusCodes.Status401Unauthorized);
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
                "Public passkey sign-in completed for PlatformUser {PlatformUserId}",
                platformUser.Id);

            return TypedResults.Ok(new PublicTokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope,
                IsNewUser = false
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Public passkey assertion verification failed");
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["assertion_response"] = [ex.Message]
            });
        }
    }
}
