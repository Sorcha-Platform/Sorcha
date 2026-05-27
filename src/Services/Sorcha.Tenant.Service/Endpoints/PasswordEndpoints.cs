// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Filters;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Password lifecycle endpoints for a signed-in user (Feature 116 US3).
/// Three operations — set, change, remove — backed by
/// <see cref="IPasswordManagementService"/>. Set bypasses the re-authentication
/// challenge requirement when the user is in bootstrap mode (zero other sign-in
/// methods). Change and Remove always require a fresh challenge token.
/// </summary>
public static class PasswordEndpoints
{
    /// <summary>Maps the password lifecycle endpoints onto the application.</summary>
    public static IEndpointRouteBuilder MapPasswordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/password")
            .WithTags("Password")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.PlatformAuth);

        group.MapPost("/set", SetPassword)
            .WithName("SetPassword")
            .WithSummary("Set an initial password")
            .WithDescription("Sets a password on a user that does not currently have one. Requires "
                + "a fresh X-Auth-Challenge token issued for SetPassword unless the user has zero "
                + "other sign-in methods (bootstrap mode — bypasses the challenge). Returns 409 if "
                + "the user already has a password.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/change", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Rotate the password")
            .WithDescription("Replaces the current password with a new one. Requires a fresh "
                + "X-Auth-Challenge token issued for ChangePassword. Returns 409 if no password is "
                + "currently set.")
            .RequireAuthChallenge(ScopedOperation.ChangePassword)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/remove", RemovePassword)
            .WithName("RemovePassword")
            .WithSummary("Clear the password")
            .WithDescription("Removes the password from the user. Requires a fresh X-Auth-Challenge "
                + "token issued for RemovePassword. Returns 409 if no password to remove or if "
                + "removing it would leave the user with zero sign-in methods.")
            .RequireAuthChallenge(ScopedOperation.RemovePassword)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>POST /api/auth/password/set — bootstrap-aware initial password.</summary>
    private static async Task<IResult> SetPassword(
        PasswordRequest request,
        IPasswordManagementService passwordService,
        IAuthChallengeRepository authChallengeRepository,
        AuthMetrics metrics,
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        var validation = ValidatePassword(request);
        if (validation is not null) return validation;

        // Bootstrap escape hatch: a user with zero sign-in methods (e.g. just
        // verified email after social-deauth, or a freshly-provisioned account
        // where the original method was removed) cannot present a re-auth
        // challenge — they have nothing to authenticate with. Allow the set
        // without the challenge in that exact case; everything else gates.
        if (!await passwordService.IsBootstrapModeAsync(platformUserId.Value, cancellationToken))
        {
            var challengeError = await ValidateAndConsumeChallengeAsync(
                httpContext,
                authChallengeRepository,
                metrics,
                logger,
                platformUserId.Value,
                ScopedOperation.SetPassword,
                cancellationToken);

            if (challengeError is not null) return challengeError;
        }

        var outcome = await passwordService.SetAsync(platformUserId.Value, request.Password, cancellationToken);
        return outcome switch
        {
            PasswordSetOutcome.Set => TypedResults.NoContent(),
            PasswordSetOutcome.AlreadySet => TypedResults.Conflict(new
            {
                error = "Password already set. Use /api/auth/password/change to rotate it."
            }),
            PasswordSetOutcome.PolicyViolation => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password does not meet platform policy requirements."]
            }),
            PasswordSetOutcome.NotFound => TypedResults.Unauthorized(),
            _ => TypedResults.Problem("Unexpected set-password outcome.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>POST /api/auth/password/change — rotate, gated by ChangePassword challenge.</summary>
    private static async Task<IResult> ChangePassword(
        PasswordRequest request,
        IPasswordManagementService passwordService,
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        var validation = ValidatePassword(request);
        if (validation is not null) return validation;

        var outcome = await passwordService.ChangeAsync(platformUserId.Value, request.Password, cancellationToken);
        return outcome switch
        {
            PasswordChangeOutcome.Changed => TypedResults.NoContent(),
            PasswordChangeOutcome.NoCurrentPassword => TypedResults.Conflict(new
            {
                error = "No password currently set. Use /api/auth/password/set to add one."
            }),
            PasswordChangeOutcome.PolicyViolation => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password does not meet platform policy requirements."]
            }),
            PasswordChangeOutcome.NotFound => TypedResults.Unauthorized(),
            _ => TypedResults.Problem("Unexpected change-password outcome.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>POST /api/auth/password/remove — clear, gated by RemovePassword challenge + floor.</summary>
    private static async Task<IResult> RemovePassword(
        IPasswordManagementService passwordService,
        HttpContext httpContext,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(httpContext, identityRepository, cancellationToken);
        if (platformUserId is null) return TypedResults.Unauthorized();

        var outcome = await passwordService.RemoveAsync(platformUserId.Value, cancellationToken);
        return outcome switch
        {
            PasswordRemoveOutcome.Removed => TypedResults.NoContent(),
            PasswordRemoveOutcome.NoCurrentPassword => TypedResults.Conflict(new
            {
                error = "No password currently set."
            }),
            PasswordRemoveOutcome.BlockedByFloor => TypedResults.Conflict(new
            {
                error = "Cannot remove the last remaining sign-in method. Add a passkey or social link first."
            }),
            PasswordRemoveOutcome.NotFound => TypedResults.Unauthorized(),
            _ => TypedResults.Problem("Unexpected remove-password outcome.", statusCode: StatusCodes.Status500InternalServerError),
        };
    }

    private static IResult? ValidatePassword(PasswordRequest? request)
    {
        if (request is null || string.IsNullOrEmpty(request.Password))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password is required."]
            });
        }
        return null;
    }

    /// <summary>
    /// Resolves the calling user's PlatformUserId from the bearer token.
    /// Falls back to <c>sub</c> → <see cref="IIdentityRepository.GetUserByIdAsync"/>
    /// when the custom <c>platform_user_id</c> claim is absent (matches the
    /// helper used in <see cref="AuthMethodsEndpoints"/> and
    /// <see cref="PasskeyEndpoints"/>).
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
    /// Inline equivalent of <see cref="RequireAuthChallengeFilter"/> for endpoints
    /// that need conditional gating — the filter runs unconditionally, so
    /// <c>/password/set</c> uses this helper to skip validation in bootstrap mode.
    /// Mirrors the filter's 5-step protocol and emits the same telemetry counters.
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
            logger.LogWarning("Missing X-Auth-Challenge on password endpoint scope={Scope}", expectedOperation);
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
