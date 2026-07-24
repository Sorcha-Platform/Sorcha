// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Filters;

/// <summary>
/// Endpoint filter that requires a valid, single-use re-authentication
/// challenge token in the <c>X-Auth-Challenge</c> header. The token MUST:
/// (1) exist, (2) belong to the calling user, (3) match the
/// <see cref="Operation"/> the endpoint is decorated for, (4) not be expired,
/// and (5) not have been previously consumed. Consumption is atomic via
/// <see cref="IAuthChallengeRepository.TryConsumeAsync"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAuthChallengeAttribute : Attribute
{
    /// <summary>Header name carrying the raw challenge token.</summary>
    public const string HeaderName = "X-Auth-Challenge";

    /// <summary>Operation the decorated endpoint authorises with this challenge.</summary>
    public ScopedOperation Operation { get; }

    /// <summary>Decorate an endpoint to require a challenge for the given operation.</summary>
    public RequireAuthChallengeAttribute(ScopedOperation operation)
    {
        Operation = operation;
    }
}

/// <summary>
/// Filter implementation invoked from the Minimal API endpoint group via
/// <see cref="RequireAuthChallengeFilterExtensions.RequireAuthChallenge"/>.
/// Kept as an explicit class (not a closure) so unit tests can exercise the
/// branches directly.
/// </summary>
public sealed class RequireAuthChallengeFilter : IEndpointFilter
{
    private readonly ScopedOperation _expectedOperation;

    /// <summary>Construct the filter for a specific scoped operation.</summary>
    public RequireAuthChallengeFilter(ScopedOperation expectedOperation)
    {
        _expectedOperation = expectedOperation;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var repository = http.RequestServices.GetRequiredService<IAuthChallengeRepository>();
        var identityRepository = http.RequestServices.GetRequiredService<IIdentityRepository>();
        var metrics = http.RequestServices.GetRequiredService<AuthMetrics>();
        var logger = http.RequestServices.GetRequiredService<ILogger<RequireAuthChallengeFilter>>();

        // Step 1: header present.
        var rawHeader = http.Request.Headers[RequireAuthChallengeAttribute.HeaderName].ToString();
        if (string.IsNullOrEmpty(rawHeader))
        {
            metrics.RecordChallengeConsumed(method: default, scope: _expectedOperation, ChallengeConsumeOutcome.Missing);
            logger.LogWarning("Missing {Header} on gated endpoint scope={Scope}",
                RequireAuthChallengeAttribute.HeaderName, _expectedOperation);
            return TypedResults.Problem(
                detail: "Missing X-Auth-Challenge header.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 2: lookup by SHA-256 hash of the raw header.
        var tokenHash = ComputeSha256Hex(rawHeader);
        var token = await repository.FindByHashAsync(tokenHash, http.RequestAborted);
        if (token is null)
        {
            metrics.RecordChallengeConsumed(default, _expectedOperation, ChallengeConsumeOutcome.Mismatch);
            return TypedResults.Problem(
                detail: "Challenge token not recognised.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 3a: caller matches token owner.
        var callerPlatformUserId = await TryGetPlatformUserIdAsync(http, identityRepository, http.RequestAborted);
        if (callerPlatformUserId is null || callerPlatformUserId.Value != token.PlatformUserId)
        {
            metrics.RecordChallengeConsumed(token.Method, _expectedOperation, ChallengeConsumeOutcome.Mismatch);
            logger.LogWarning(
                "Challenge token presented by wrong principal expected={Expected} got={Got}",
                token.PlatformUserId, callerPlatformUserId);
            return TypedResults.Problem(
                detail: "Challenge token does not belong to the calling user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 3b: scope matches.
        if (token.ScopedOperation != _expectedOperation)
        {
            metrics.RecordChallengeConsumed(token.Method, _expectedOperation, ChallengeConsumeOutcome.Mismatch);
            logger.LogWarning(
                "Challenge scope mismatch issued={Issued} required={Required}",
                token.ScopedOperation, _expectedOperation);
            return TypedResults.Problem(
                detail: "Challenge token was issued for a different operation.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 4: not expired.
        if (token.ExpiresAt < DateTimeOffset.UtcNow)
        {
            metrics.RecordChallengeConsumed(token.Method, _expectedOperation, ChallengeConsumeOutcome.Expired);
            return TypedResults.Problem(
                detail: "Challenge token has expired.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 5: atomic consume. The repository update returns 0 rows when
        // ConsumedAt is already non-null — that is a replay. The consume +
        // request execution happens before the mutation handler runs, so a
        // failure between consume and mutation could leave the token spent
        // without effect. That trade is intentional: we never run the
        // mutation with an unconsumed token (avoids the worse failure mode).
        var consumed = await repository.TryConsumeAsync(token.Id, DateTimeOffset.UtcNow, http.RequestAborted);
        if (!consumed)
        {
            metrics.RecordChallengeConsumed(token.Method, _expectedOperation, ChallengeConsumeOutcome.Replay);
            logger.LogWarning(
                "Challenge token replay attempt for {PlatformUserId} scope={Scope}",
                token.PlatformUserId, _expectedOperation);
            return TypedResults.Problem(
                detail: "Challenge token has already been used.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        metrics.RecordChallengeConsumed(token.Method, _expectedOperation, ChallengeConsumeOutcome.Success);
        return await next(context);
    }

    private static async Task<Guid?> TryGetPlatformUserIdAsync(
        HttpContext http,
        IIdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        // Active sessions carry PlatformUserId as a custom claim (set by
        // TokenService.GenerateUserTokenAsync). Falls back to sub →
        // IIdentityRepository.GetUserByIdAsync.PlatformUserId for tokens
        // that only carry the canonical sub claim (e.g. test JWTs from
        // TestAuthHandler, or any future token issuer that omits the
        // custom claim). Matches the resolver shape used by
        // AuthMethodsEndpoints and PasskeyEndpoints.
        var pid = http.User.FindFirst("platform_user_id")?.Value
                  ?? http.User.FindFirst("pid")?.Value;
        if (Guid.TryParse(pid, out var id)) return id;

        var sub = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userIdentityId)) return null;

        var user = await identityRepository.GetUserByIdAsync(userIdentityId, cancellationToken);
        return user?.PlatformUserId;
    }

    private static string ComputeSha256Hex(string raw)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Endpoint-builder extensions for applying <see cref="RequireAuthChallengeFilter"/>.
/// Use <c>.RequireAuthChallenge(ScopedOperation.X)</c> in the same fluent
/// chain as <c>.RequireAuthorization()</c> on the endpoint registration.
/// </summary>
public static class RequireAuthChallengeFilterExtensions
{
    /// <summary>
    /// Require a valid <c>X-Auth-Challenge</c> token scoped to <paramref name="operation"/>
    /// before the endpoint handler runs.
    /// </summary>
    public static TBuilder RequireAuthChallenge<TBuilder>(this TBuilder builder, ScopedOperation operation)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(epb =>
        {
            epb.FilterFactories.Add((ctx, next) =>
            {
                var filter = new RequireAuthChallengeFilter(operation);
                return new EndpointFilterDelegate((invocationContext) => filter.InvokeAsync(invocationContext, next));
            });
        });
        return builder;
    }
}
