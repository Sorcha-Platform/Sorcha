// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Platform user device endpoints for Feature 114 (Citizen Wallet PWA) recovery
/// flows. Mounted under <c>/api/v1/me/devices</c> and reachable from the main
/// Sorcha web UI when a citizen has lost their wallet device and needs to revoke
/// it from elsewhere ("I lost my phone, log in here, see and revoke devices").
/// </summary>
/// <remarks>
/// PR1 (T113-T115) implements the local Tenant-side surface only — list and
/// revoke flip <see cref="PlatformUserDevice.Status"/> on the Tenant database.
/// PR2 (T116-T117) extends <see cref="IPlatformUserDeviceService.RevokeAsync"/>
/// to dispatch a service-to-service call into the Wallet Service which flips
/// the status-list bit and broadcasts a SignalR event.
/// </remarks>
public static class PlatformUserDeviceEndpoints
{
    /// <summary>Maps the platform user device endpoints.</summary>
    public static IEndpointRouteBuilder MapPlatformUserDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/me/devices")
            .WithTags("Citizen Wallet Devices")
            .RequireAuthorization();

        group.MapGet("/", ListDevices)
            .WithName("ListMyPlatformUserDevices")
            .WithSummary("List the authenticated citizen's enrolled wallet devices")
            .WithDescription(
                "Mirror of GET /api/v1/wallet/devices reachable through the main "
                + "Sorcha web UI for recovery flows. Returns active and revoked "
                + "devices ordered by enrolment date descending.")
            .Produces<DeviceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/{deviceId:guid}", RevokeDevice)
            .WithName("RevokeMyPlatformUserDevice")
            .WithSummary("Revoke a citizen wallet device")
            .WithDescription(
                "Marks the device as revoked on the Tenant Service. The Wallet "
                + "Service status-list bit is flipped via the existing service-"
                + "to-service channel (PR2). Returns 204 on success or first "
                + "revocation, and 404 when the device does not exist or is not "
                + "owned by the caller (the two cases are intentionally "
                + "indistinguishable to avoid leaking device existence).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListDevices(
        HttpContext context,
        IPlatformUserDeviceService deviceService,
        IIdentityRepository identityRepository,
        CancellationToken ct)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(context, identityRepository, ct);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var devices = await deviceService.ListAsync(platformUserId.Value, ct);

        return TypedResults.Ok(new DeviceListResponse
        {
            Devices = devices.Select(MapToSummary).ToList()
        });
    }

    private static async Task<IResult> RevokeDevice(
        Guid deviceId,
        HttpContext context,
        IPlatformUserDeviceService deviceService,
        IIdentityRepository identityRepository,
        CancellationToken ct)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(context, identityRepository, ct);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var revoked = await deviceService.RevokeAsync(deviceId, platformUserId.Value, ct);
        return revoked is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static DeviceSummary MapToSummary(PlatformUserDevice device) => new()
    {
        DeviceId = device.Id,
        Label = device.Label,
        Platform = device.Platform,
        Status = device.Status == PlatformUserDeviceStatus.Revoked
            ? DeviceStatus.Revoked
            : DeviceStatus.Active,
        EnrolledAt = device.EnrolledAt,
        RevokedAt = device.RevokedAt,
        LastSeenAt = device.LastSeenAt,
        DelegationExpiresAt = device.DelegationExpiresAt
    };

    /// <summary>
    /// Resolves PlatformUserId from the bearer claims. Web-UI JWTs carry
    /// <c>sub = UserIdentity.Id</c> with the PlatformUserId either riding on a
    /// custom <c>platform_user_id</c>/<c>pid</c> claim or recovered via the
    /// identity repository. Mirrors the resolution pattern in
    /// AuthChallengeEndpoints.
    /// </summary>
    private static async Task<Guid?> ResolvePlatformUserIdAsync(
        HttpContext context,
        IIdentityRepository identityRepository,
        CancellationToken ct)
    {
        var pidClaim = context.User.FindFirst("platform_user_id")?.Value
            ?? context.User.FindFirst("pid")?.Value;
        if (Guid.TryParse(pidClaim, out var pid) && pid != Guid.Empty)
        {
            return pid;
        }

        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(sub, out var userIdentityId)) return null;

        var user = await identityRepository.GetUserByIdAsync(userIdentityId, ct);
        if (user is null || user.PlatformUserId == Guid.Empty) return null;
        return user.PlatformUserId;
    }
}
