// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Filters;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Telemetry;

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

        group.MapPut("/credentials/{id:guid}", RenameCredential)
            .WithName("PasskeyRenameCredential")
            .WithSummary("Rename a passkey credential")
            .WithDescription("Updates the user-visible display name of an Active passkey credential. "
                + "Disabled or Revoked credentials cannot be renamed (returns 409). No re-authentication "
                + "challenge is required for rename.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/credentials/{id:guid}", DeleteCredential)
            .WithName("PasskeyDeleteCredential")
            .WithSummary("Soft-revoke a passkey credential")
            .WithDescription("Transitions an Active passkey to Revoked (preserving the audit row). "
                + "Active passkeys require a fresh re-authentication challenge in the X-Auth-Challenge "
                + "header. Disabled passkeys (already non-functional) bypass the challenge requirement. "
                + "Removing a credential that would leave the user with zero sign-in methods is rejected with 409.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        // Feature 060: Service-to-service endpoint for recovery key wrapping
        app.MapGet("/api/users/{userId}/passkeys/recovery-key", GetRecoveryPublicKey)
            .WithTags("Passkey")
            .WithName("GetPasskeyRecoveryKey")
            .WithSummary("Get passkey public key for recovery key wrapping")
            .WithDescription("Returns the primary passkey's public key for the specified user. "
                + "Used by Wallet Service for recovery key wrapping during wallet creation.")
            .RequireAuthorization()
            .Produces<PasskeyRecoveryKeyResponse>()
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
        var platformUserIdClaim = user.FindFirst("platform_user_id")?.Value;
        if (platformUserIdClaim is null || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
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

        // Check credential limit
        var existingCredentials = await passkeyService.GetCredentialsByOwnerAsync(platformUserId, cancellationToken);
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
                platformUserId,
                request.DisplayName,
                existingCredentialIds,
                cancellationToken);

            logger.LogInformation("Passkey registration options created for PlatformUser {PlatformUserId}", platformUserId);

            return TypedResults.Ok(new PasskeyRegistrationOptionsResponse
            {
                TransactionId = result.TransactionId,
                Options = JsonDocument.Parse(result.Options.ToJson()).RootElement
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create passkey registration options for PlatformUser {PlatformUserId}", platformUserId);
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
        var platformUserIdClaim = user.FindFirst("platform_user_id")?.Value;
        if (platformUserIdClaim is null || !Guid.TryParse(platformUserIdClaim, out var platformUserId))
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

            logger.LogInformation("Passkey credential registered for PlatformUser {PlatformUserId}: {CredentialId}",
                platformUserId, credential.Id);

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
            logger.LogWarning(ex, "Passkey registration verification failed for PlatformUser {PlatformUserId}", platformUserId);
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
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var resolvedId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (resolvedId is null) return TypedResults.Unauthorized();
        var platformUserId = resolvedId.Value;

        var credentials = await passkeyService.GetCredentialsByOwnerAsync(platformUserId, cancellationToken);

        // Feature 116 US2 (T054): exclude soft-revoked rows from the list. The
        // service stays inclusive so callers like LoginService/PublicPasskeyEndpoints
        // can still resolve historical rows when needed.
        var response = new PasskeyCredentialListResponse
        {
            Credentials = credentials
                .Where(c => c.Status != CredentialStatus.Revoked)
                .Select(c => new PasskeyCredentialResponse
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
    /// GET /api/users/{userId}/passkeys/recovery-key — get primary passkey public key for recovery wrapping.
    /// </summary>
    private static async Task<IResult> GetRecoveryPublicKey(
        string userId,
        IPasskeyService passkeyService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var platformUserId))
        {
            return TypedResults.BadRequest("Invalid user ID format");
        }

        var credentials = await passkeyService.GetCredentialsByOwnerAsync(platformUserId, cancellationToken);
        var activeCredential = credentials
            .Where(c => c.Status == CredentialStatus.Active)
            .OrderBy(c => c.CreatedAt) // Primary = oldest active
            .FirstOrDefault();

        if (activeCredential is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PasskeyRecoveryKeyResponse
        {
            CredentialId = Convert.ToBase64String(activeCredential.CredentialId),
            PublicKeyCose = Convert.ToBase64String(activeCredential.PublicKeyCose),
            Algorithm = MapCoseAlgorithm(activeCredential.AaGuid)
        });
    }

    private static string MapCoseAlgorithm(Guid aaGuid)
    {
        // Default to ES256 (P-256) which is the most common WebAuthn algorithm
        return "ES256";
    }

    /// <summary>
    /// PUT /api/passkey/credentials/{id} — rename a passkey credential (Feature 116 US2).
    /// </summary>
    private static async Task<IResult> RenameCredential(
        Guid id,
        PasskeyRenameRequest request,
        IPasskeyService passkeyService,
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        if (request is null || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name is required"]
            });
        }

        var trimmed = request.DisplayName.Trim();
        if (trimmed.Length > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["display_name"] = ["Display name must be 100 characters or fewer"]
            });
        }

        var outcome = await passkeyService.RenameCredentialAsync(id, platformUserId.Value, trimmed, cancellationToken);
        return outcome switch
        {
            PasskeyRenameOutcome.NotFound => TypedResults.NotFound(),
            PasskeyRenameOutcome.BlockedByDisabled => TypedResults.Conflict(new { error = "Disabled passkeys cannot be renamed." }),
            PasskeyRenameOutcome.BlockedByRevoked => TypedResults.NotFound(),
            PasskeyRenameOutcome.Renamed => TypedResults.NoContent(),
            _ => TypedResults.Problem("Unexpected rename outcome.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// DELETE /api/passkey/credentials/{id} — soft-revoke a passkey credential (Feature 116 US2).
    /// </summary>
    /// <remarks>
    /// Active passkeys require a fresh re-authentication challenge token in the
    /// <c>X-Auth-Challenge</c> header (validated and consumed inline so the gating
    /// can branch on credential status — Disabled passkeys are already non-functional
    /// and bypass the challenge requirement per design §6.4 / contract §delete).
    /// </remarks>
    private static async Task<IResult> DeleteCredential(
        Guid id,
        IPasskeyService passkeyService,
        IAuthChallengeRepository authChallengeRepository,
        AuthMetrics metrics,
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        // Read prior status to decide whether the challenge gate applies. This
        // read intentionally races with concurrent Active→Disabled transitions;
        // the service layer re-reads under its own SaveChanges so the audit
        // reason string always reflects the actual state at revocation time.
        var existing = await passkeyService.GetCredentialAsync(id, platformUserId.Value, cancellationToken);
        if (existing is null || existing.Status == CredentialStatus.Revoked)
        {
            return TypedResults.NotFound();
        }

        if (existing.Status == CredentialStatus.Active)
        {
            var challengeError = await ValidateAndConsumeChallengeAsync(
                httpContext,
                authChallengeRepository,
                metrics,
                logger,
                platformUserId.Value,
                ScopedOperation.RemoveAuthMethod,
                cancellationToken);

            if (challengeError is not null) return challengeError;
        }

        var outcome = await passkeyService.RevokeCredentialAsync(id, platformUserId.Value, cancellationToken);
        return outcome switch
        {
            PasskeyRevocationOutcome.NotFound or
            PasskeyRevocationOutcome.AlreadyRevoked => TypedResults.NotFound(),
            PasskeyRevocationOutcome.BlockedByFloor => TypedResults.Conflict(new
            {
                error = "Cannot remove the last remaining sign-in method. Add a password, social login, or another passkey first."
            }),
            PasskeyRevocationOutcome.RevokedFromActive or
            PasskeyRevocationOutcome.RevokedFromDisabled => TypedResults.NoContent(),
            _ => TypedResults.Problem("Unexpected revocation outcome.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>
    /// Resolves the calling user's PlatformUserId from the bearer token.
    /// Falls back to the canonical sub claim → IIdentityRepository lookup
    /// when the custom <c>platform_user_id</c> claim is absent (matches the
    /// helper used in <see cref="AuthMethodsEndpoints"/> so test JWT shapes
    /// missing the custom claim still resolve correctly).
    /// </summary>
    private static async Task<Guid?> ResolvePlatformUserIdAsync(
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var pidClaim = httpContext.User.FindFirst("platform_user_id")?.Value
                       ?? httpContext.User.FindFirst("pid")?.Value;
        if (Guid.TryParse(pidClaim, out var pid)) return pid;

        var sub = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userIdentityId)) return null;

        var user = await identityRepository.GetUserByIdAsync(userIdentityId, cancellationToken);
        return user?.PlatformUserId;
    }

    /// <summary>
    /// Inline implementation of the <see cref="RequireAuthChallengeFilter"/>
    /// pipeline for endpoints that gate conditionally on resource state. The
    /// 5-step protocol matches the filter (header → lookup → owner+scope →
    /// expiry → atomic consume) and emits the same telemetry. Returns null
    /// on success and the failure <see cref="IResult"/> otherwise.
    /// </summary>
    private static async Task<IResult?> ValidateAndConsumeChallengeAsync(
        HttpContext httpContext,
        IAuthChallengeRepository repository,
        AuthMetrics metrics,
        ILogger logger,
        Guid callerPlatformUserId,
        ScopedOperation expectedOperation,
        CancellationToken cancellationToken)
    {
        var rawHeader = httpContext.Request.Headers[RequireAuthChallengeAttribute.HeaderName].ToString();
        if (string.IsNullOrEmpty(rawHeader))
        {
            metrics.RecordChallengeConsumed(default, expectedOperation, ChallengeConsumeOutcome.Missing);
            logger.LogWarning("Missing X-Auth-Challenge on passkey delete scope={Scope}", expectedOperation);
            return TypedResults.Problem(
                detail: "Missing X-Auth-Challenge header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var tokenHash = ComputeSha256Hex(rawHeader);
        var token = await repository.FindByHashAsync(tokenHash, cancellationToken);
        if (token is null)
        {
            metrics.RecordChallengeConsumed(default, expectedOperation, ChallengeConsumeOutcome.Mismatch);
            return TypedResults.Problem(
                detail: "Challenge token not recognised.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (token.PlatformUserId != callerPlatformUserId)
        {
            metrics.RecordChallengeConsumed(token.Method, expectedOperation, ChallengeConsumeOutcome.Mismatch);
            return TypedResults.Problem(
                detail: "Challenge token does not belong to the calling user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (token.ScopedOperation != expectedOperation)
        {
            metrics.RecordChallengeConsumed(token.Method, expectedOperation, ChallengeConsumeOutcome.Mismatch);
            return TypedResults.Problem(
                detail: "Challenge token was issued for a different operation.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (token.ExpiresAt < DateTimeOffset.UtcNow)
        {
            metrics.RecordChallengeConsumed(token.Method, expectedOperation, ChallengeConsumeOutcome.Expired);
            return TypedResults.Problem(
                detail: "Challenge token has expired.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var consumed = await repository.TryConsumeAsync(token.Id, DateTimeOffset.UtcNow, cancellationToken);
        if (!consumed)
        {
            metrics.RecordChallengeConsumed(token.Method, expectedOperation, ChallengeConsumeOutcome.Replay);
            return TypedResults.Problem(
                detail: "Challenge token has already been used.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        metrics.RecordChallengeConsumed(token.Method, expectedOperation, ChallengeConsumeOutcome.Success);
        return null;
    }

    private static string ComputeSha256Hex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
