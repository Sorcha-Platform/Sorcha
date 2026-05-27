// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.CitizenStatusList;
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

        group.MapGet("/has-any", HasAnyDevice)
            .WithName("CheckMyPlatformUserHasAnyDevice")
            .WithSummary("Has the authenticated citizen paired at least one active device?")
            .WithDescription(
                "Aggregate read used by F128 cold-start surfaces — the wallet PWA "
                + "pairing takeover (FR-010) and the Sorcha Web nag banner (FR-024). "
                + "Returns hasAnyDevice + the latest active-device enrolment timestamp; "
                + "does not leak the per-device list.")
            .Produces<HasAnyDeviceResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> HasAnyDevice(
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

        var (hasAny, latestEnrolledAt) = await deviceService.HasAnyAsync(platformUserId.Value, ct);

        return TypedResults.Ok(new HasAnyDeviceResponse
        {
            HasAnyDevice = hasAny,
            LatestEnrolledAt = latestEnrolledAt,
        });
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
        ICitizenStatusListClient statusListClient,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var platformUserId = await ResolvePlatformUserIdAsync(context, identityRepository, ct);
        if (platformUserId is null)
        {
            return TypedResults.Unauthorized();
        }

        var revoked = await deviceService.RevokeAsync(deviceId, platformUserId.Value, ct);
        if (revoked is null)
        {
            return TypedResults.NotFound();
        }

        // Tenant row flipped — now propagate to Wallet so the status-list bit is
        // set and any wallet PWA still connected for this user receives the
        // SignalR DeviceRevoked broadcast and locks itself.
        var orgId = ResolveOrgId(context);
        if (orgId is null)
        {
            // No org context on the JWT — Tenant row is revoked, but we cannot
            // address the Wallet status list without orgId. Log + return success;
            // the next periodic re-sign will not change this device's bit, but
            // a citizen with no org context shouldn't have an enrolled wallet
            // device in the first place. Treated as a soft data-integrity warning.
            loggerFactory.CreateLogger("PlatformUserDeviceEndpoints").LogWarning(
                "Revoked PlatformUserDevice {DeviceId} for platformUser={PlatformUserId} but JWT had " +
                "no org_id claim — status-list bit NOT flipped. Investigate token issuance.",
                deviceId, platformUserId.Value);
            return TypedResults.NoContent();
        }

        try
        {
            await statusListClient.RevokeAsync(
                orgId.Value,
                revoked.StatusListId,
                revoked.StatusListIndex,
                deviceId,
                platformUserId.Value,
                ct);
        }
        catch (HttpRequestException ex)
        {
            // Tenant row is revoked but Wallet flip failed — best-effort. The
            // hourly CitizenStatusListPublisherService will not pick this up
            // (it only re-signs existing state), so a follow-up reconciliation
            // job is desirable. For now: log and surface 502 so the caller
            // can retry — the Tenant flip is idempotent.
            loggerFactory.CreateLogger("PlatformUserDeviceEndpoints").LogError(
                ex,
                "Wallet Service rejected status-list flip for deviceId={DeviceId} "
                + "(platformUser={PlatformUserId}, org={OrgId}, list={ListId}#{Index}). "
                + "Tenant row IS revoked; caller should retry.",
                deviceId, platformUserId.Value, orgId.Value,
                revoked.StatusListId, revoked.StatusListIndex);
            return TypedResults.Problem(
                title: "Status-list flip failed",
                detail: "Tenant row is revoked but the wallet status-list bit could not be flipped. Retry to complete revocation.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return TypedResults.NoContent();
    }

    private static Guid? ResolveOrgId(HttpContext context)
    {
        var raw = context.User.FindFirst("org_id")?.Value
            ?? context.User.FindFirst("organization_id")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
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
