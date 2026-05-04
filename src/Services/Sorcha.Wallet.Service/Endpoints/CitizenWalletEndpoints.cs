// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Citizen wallet PWA endpoints (Feature 114). Mounted under <c>/api/v1/wallet/*</c>
/// and require the JWT to carry the <see cref="Sorcha.CitizenWallet.Abstractions.Constants.JwtAudiences.CitizenWallet"/>
/// audience — enforced via JWT bearer pipeline configuration.
/// </summary>
public static class CitizenWalletEndpoints
{
    private const string CitizenWalletPolicyName = "CitizenWalletAudience";

    /// <summary>Maps the citizen wallet endpoints.</summary>
    public static IEndpointRouteBuilder MapCitizenWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallet")
            .WithTags("Citizen Wallet")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapPost("/devices/enrol", EnrolDevice)
            .WithName("EnrolCitizenDevice")
            .WithSummary("Enrol a citizen wallet device (PWA)")
            .WithDescription(
                "Generates (if absent) the citizen's holder key under sorcha:citizen-holder, " +
                "issues a device delegation credential signed by the holder key, allocates a " +
                "status-list slot, persists the device on the Tenant Service, and returns " +
                "everything the wallet needs to operate offline. Rate-limited (Strict).")
            .Produces<DeviceEnrolmentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/credentials", ListCredentials)
            .WithName("ListCitizenCredentials")
            .WithSummary("Full credential snapshot for the authenticated citizen")
            .WithDescription(
                "Returns every credential currently issued to the citizen. Used by a " +
                "freshly-enrolled wallet to seed its cache; subsequent updates flow " +
                "through GET /api/v1/wallet/sync.")
            .Produces<CredentialListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/devices/renew-delegation", RenewDelegation)
            .WithName("RenewCitizenDeviceDelegation")
            .WithSummary("Renew the device delegation credential")
            .WithDescription(
                "Idempotent re-issuance of the holder→device delegation, signed by " +
                "the citizen's holder key. Wallets call this when their current " +
                "delegation is approaching expiry (within 30 days). Returns 404 if " +
                "the device is unknown or not owned by the caller.")
            .Produces<DelegationRenewalResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/sync", SyncCredentials)
            .WithName("SyncCitizenWallet")
            .WithSummary("Pull credential and delegation deltas since the last sync")
            .WithDescription(
                "Returns adds/revokes/replacements since the supplied opaque cursor. " +
                "Omit the cursor on first sync. Cursors older than 30 days return 410 — " +
                "the wallet should fall back to GET /credentials.")
            .Produces<SyncResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<IResult> RenewDelegation(
        [FromBody] DelegationRenewalRequest request,
        HttpContext context,
        IDelegationRenewalService renewal,
        CancellationToken ct)
    {
        var (platformUserId, citizenWallet, organizationId) = ResolveCitizenContext(context.User);
        if (platformUserId is null || citizenWallet is null || organizationId is null)
        {
            return Results.Unauthorized();
        }

        if (request.DeviceId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["deviceId"] = new[] { "deviceId is required." }
            });
        }

        var response = await renewal.RenewAsync(
            request.DeviceId, platformUserId.Value, citizenWallet, organizationId.Value, ct);

        return response is null
            ? Results.NotFound()
            : Results.Ok(response);
    }

    private static async Task<IResult> ListCredentials(
        HttpContext context,
        ICitizenSyncService sync,
        IHolderKeyService holderKeys,
        CancellationToken ct)
    {
        var (platformUserId, citizenWallet, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null || citizenWallet is null) return Results.Unauthorized();

        var holderKeyId = await holderKeys.GetHolderJwkThumbprintAsync(citizenWallet, ct);
        var snapshot = await sync.ListAllCredentialsAsync(platformUserId.Value, holderKeyId, ct);
        return Results.Ok(snapshot);
    }

    private static async Task<IResult> SyncCredentials(
        HttpContext context,
        [FromQuery] string? since,
        ICitizenSyncService sync,
        IHolderKeyService holderKeys,
        CancellationToken ct)
    {
        var (platformUserId, citizenWallet, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null || citizenWallet is null) return Results.Unauthorized();

        var holderKeyId = await holderKeys.GetHolderJwkThumbprintAsync(citizenWallet, ct);
        var delta = await sync.ComposeDeltaAsync(platformUserId.Value, holderKeyId, since, ct);
        if (delta is null)
        {
            return Results.Problem(
                title: "Sync cursor expired",
                detail: "The supplied sync cursor is older than the maximum cursor age. " +
                        "Re-seed the cache via GET /api/v1/wallet/credentials.",
                statusCode: StatusCodes.Status410Gone);
        }
        return Results.Ok(delta);
    }

    private static async Task<IResult> EnrolDevice(
        [FromBody] DeviceEnrolmentRequest request,
        HttpContext context,
        IValidator<DeviceEnrolmentRequest> validator,
        IHolderKeyService holderKeys,
        IDeviceDelegationIssuer issuer,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        IPlatformUserDeviceClient deviceClient,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var (platformUserId, citizenWallet, organizationId) = ResolveCitizenContext(context.User);
        if (platformUserId is null || citizenWallet is null || organizationId is null)
        {
            return Results.Unauthorized();
        }

        var orgSigningWallet = await orgWalletResolver.ResolveAsync(organizationId.Value, ct);

        var delegation = await issuer.IssueAsync(
            platformUserId.Value,
            citizenWallet,
            organizationId.Value,
            orgSigningWallet,
            request.DevicePublicJwk,
            request.DeviceLabel,
            request.Platform,
            ct);

        var deviceJwkJson = System.Text.Json.JsonSerializer.Serialize(request.DevicePublicJwk);
        if (deviceJwkJson.Length > 512)
        {
            return Results.Problem(
                title: "Device JWK too large",
                detail: "Canonical JWK JSON must be ≤ 512 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var deviceThumbprint = ComputeEcThumbprint(request.DevicePublicJwk);

        try
        {
            var registered = await deviceClient.RegisterAsync(
                platformUserId.Value,
                request.DeviceLabel,
                deviceThumbprint,
                deviceJwkJson,
                request.Platform,
                request.UserAgent,
                delegation.ExpiresAt,
                delegation.Jti,
                delegation.StatusListId,
                delegation.StatusListIndex,
                ct);

            logger.LogInformation(
                "Citizen device enrolled platformUser={PlatformUserId} deviceId={DeviceId} jti={Jti}",
                platformUserId, registered.DeviceId, delegation.Jti);

            return Results.Ok(new DeviceEnrolmentResponse
            {
                DeviceId = registered.DeviceId,
                DelegationCredential = delegation.CompactJwt,
                HolderPublicJwk = ParseHolderJwk(delegation.HolderPublicJwk),
                StatusListUri = delegation.StatusListUri,
                StatusListIndex = delegation.StatusListIndex,
                DelegationExpiresAt = delegation.ExpiresAt
            });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Tenant Service rejected device registration for platformUser {PlatformUserId}", platformUserId);
            return Results.Problem(
                title: "Device registration failed",
                detail: "The platform device registry rejected the request.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static (Guid? platformUserId, string? walletAddress, Guid? organizationId) ResolveCitizenContext(ClaimsPrincipal user)
    {
        Guid? platformUserId = Guid.TryParse(
            user.FindFirstValue("platform_user_id") ?? user.FindFirstValue(ClaimTypes.NameIdentifier),
            out var pid) ? pid : null;

        var walletAddress = user.FindFirstValue("wallet_address");
        if (string.IsNullOrWhiteSpace(walletAddress)) walletAddress = null;

        Guid? organizationId = Guid.TryParse(user.FindFirstValue(TokenClaimConstants.OrgId), out var oid) ? oid : null;

        return (platformUserId, walletAddress, organizationId);
    }

    private static EcP256PublicJwk ParseHolderJwk(System.Text.Json.JsonElement jwk)
    {
        return new EcP256PublicJwk
        {
            Kty = jwk.GetProperty("kty").GetString() ?? "EC",
            Crv = jwk.GetProperty("crv").GetString() ?? "P-256",
            X = jwk.GetProperty("x").GetString() ?? string.Empty,
            Y = jwk.TryGetProperty("y", out var y) ? y.GetString() ?? string.Empty : string.Empty
        };
    }

    private static string ComputeEcThumbprint(EcP256PublicJwk jwk)
    {
        var canonical = $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"{jwk.Kty}\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return System.Buffers.Text.Base64Url.EncodeToString(hash);
    }
}
