// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Passkey credential registration and management API endpoints.
/// </summary>
public static class PasskeyEndpoints
{
    /// <summary>
    /// Maximum number of passkey credentials allowed per user.
    /// </summary>
    private const int MaxCredentialsPerUser = 10;

    /// <summary>
    /// Maps passkey endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/passkey")
            .WithTags("Passkey");

        group.MapPost("/register/options", RegisterOptions)
            .WithName("PasskeyRegisterOptions")
            .WithSummary("Generate passkey registration challenge")
            .WithDescription("Creates FIDO2 credential creation options for registering a new passkey. "
                + "Returns a transaction ID and challenge options to pass to the browser WebAuthn API.")
            .RequireAuthorization()
            .Produces<PasskeyRegistrationOptionsResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/register/verify", RegisterVerify)
            .WithName("PasskeyRegisterVerify")
            .WithSummary("Verify passkey registration attestation")
            .WithDescription("Verifies the attestation response from the authenticator and creates a new passkey credential. "
                + "The transaction ID must match a pending registration challenge.")
            .RequireAuthorization()
            .Produces<PasskeyCredentialResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/credentials", ListCredentials)
            .WithName("PasskeyListCredentials")
            .WithSummary("List user's passkey credentials")
            .WithDescription("Returns all passkey credentials registered by the current user.")
            .RequireAuthorization()
            .Produces<PasskeyCredentialListResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/credentials/{id:guid}", DeleteCredential)
            .WithName("PasskeyDeleteCredential")
            .WithSummary("Revoke a passkey credential")
            .WithDescription("Revokes a passkey credential, preventing its future use for authentication. "
                + "Cannot revoke the last authentication method (must have TOTP or other passkeys).")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// POST /api/passkey/register/options — generate passkey registration challenge.
    /// </summary>
    private static async Task<IResult> RegisterOptions(
        PasskeyRegisterOptionsRequest request,
        IPasskeyService passkeyService,
        ClaimsPrincipal user,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var orgIdClaim = user.FindFirst("org_id")?.Value;
        Guid? organizationId = orgIdClaim is not null && Guid.TryParse(orgIdClaim, out var orgId)
            ? orgId
            : null;

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name is required"]
            });
        }

        // Check credential limit
        var existingCredentials = await passkeyService.GetCredentialsByOwnerAsync(OwnerTypes.OrgUser, userId, cancellationToken);
        if (existingCredentials.Count >= MaxCredentialsPerUser)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = [$"Maximum of {MaxCredentialsPerUser} passkey credentials reached"]
            });
        }

        var existingCredentialIds = existingCredentials
            .Select(c => c.CredentialId)
            .ToList();

        try
        {
            var result = await passkeyService.CreateRegistrationOptionsAsync(
                OwnerTypes.OrgUser,
                userId,
                organizationId,
                request.DisplayName,
                existingCredentialIds,
                cancellationToken);

            logger.LogInformation("Passkey registration options created for user {UserId}", userId);

            return TypedResults.Ok(new PasskeyRegistrationOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = result.Options
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create passkey registration options for user {UserId}", userId);
            return TypedResults.Problem("Failed to create registration options.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// POST /api/passkey/register/verify — verify passkey registration attestation.
    /// </summary>
    private static async Task<IResult> RegisterVerify(
        PasskeyRegisterVerifyRequest request,
        IPasskeyService passkeyService,
        ClaimsPrincipal user,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
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

        try
        {
            var credential = await passkeyService.VerifyRegistrationAsync(
                request.TransactionId,
                request.AttestationResponse,
                persist: true,
                cancellationToken);

            logger.LogInformation("Passkey credential registered for user {UserId}: {CredentialId}",
                userId, credential.Id);

            return TypedResults.Created($"/api/passkey/credentials/{credential.Id}", new PasskeyCredentialResponse
            {
                Id = credential.Id,
                DisplayName = credential.DisplayName,
                DeviceType = credential.DeviceType,
                Status = credential.Status.ToString(),
                CreatedAt = credential.CreatedAt,
                LastUsedAt = credential.LastUsedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Passkey registration verification failed for user {UserId}", userId);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["attestation_response"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// GET /api/passkey/credentials — list the current user's passkey credentials.
    /// </summary>
    private static async Task<IResult> ListCredentials(
        IPasskeyService passkeyService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var credentials = await passkeyService.GetCredentialsByOwnerAsync(OwnerTypes.OrgUser, userId, cancellationToken);

        var response = new PasskeyCredentialListResponse
        {
            Credentials = credentials.Select(c => new PasskeyCredentialResponse
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                DeviceType = c.DeviceType,
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                LastUsedAt = c.LastUsedAt
            }).ToList(),
            MaxCredentials = MaxCredentialsPerUser
        };

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// DELETE /api/passkey/credentials/{id} — revoke a passkey credential.
    /// </summary>
    private static async Task<IResult> DeleteCredential(
        Guid id,
        IPasskeyService passkeyService,
        ITotpService totpService,
        ClaimsPrincipal user,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var userIdClaim = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return TypedResults.Unauthorized();
        }

        // Check if this would leave the user with no auth methods.
        // Note: check-then-act is not fully atomic but rate limiting (5/min/IP) mitigates concurrent abuse.
        var credentials = await passkeyService.GetCredentialsByOwnerAsync(OwnerTypes.OrgUser, userId, cancellationToken);
        var activeCredentials = credentials.Where(c => c.Status == CredentialStatus.Active).ToList();
        var totpStatus = await totpService.GetStatusAsync(userId, cancellationToken);

        // If this is the only active passkey and TOTP is not enabled, prevent deletion
        var isTargetActive = activeCredentials.Any(c => c.Id == id);
        if (isTargetActive && activeCredentials.Count == 1 && !totpStatus.IsEnabled)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Cannot revoke the last authentication method. Enable TOTP or register another passkey first."]
            });
        }

        var revoked = await passkeyService.RevokeCredentialAsync(id, OwnerTypes.OrgUser, userId, cancellationToken);

        if (!revoked)
        {
            return TypedResults.NotFound();
        }

        logger.LogInformation("Passkey credential {CredentialId} revoked for user {UserId}", id, userId);

        return TypedResults.NoContent();
    }
}
