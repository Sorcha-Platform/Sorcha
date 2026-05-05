// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Internal service-to-service endpoints (no authentication — accessed only by API Gateway).
/// </summary>
public static class InternalEndpoints
{
    /// <summary>
    /// Maps internal endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/internal")
            .WithTags("Internal")
            .ExcludeFromDescription(); // Hide from public API docs

        group.MapGet("/resolve-domain/{domain}", ResolveDomain)
            .WithName("ResolveDomain")
            .WithSummary("Resolve custom domain to organization subdomain")
            .WithDescription("Looks up a verified custom domain mapping and returns the corresponding organization subdomain. "
                + "Used internally by the API Gateway for domain-based routing.")
            .RequireAuthorization("RequireService");

        // Feature 114: Citizen wallet device enrolment bridge.
        group.MapPost("/platform-user-devices", RegisterPlatformUserDevice)
            .WithName("RegisterPlatformUserDevice")
            .WithSummary("Register or refresh a citizen wallet device")
            .WithDescription("Called by Wallet Service after issuing a device delegation credential. "
                + "Idempotent on (PlatformUserId, DevicePublicJwkThumbprint).")
            .RequireAuthorization("RequireService");

        // Feature 114: device lookup for delegation renewal flow.
        group.MapGet("/platform-user-devices/{deviceId:guid}", GetPlatformUserDevice)
            .WithName("GetPlatformUserDevice")
            .WithSummary("Look up a citizen wallet device by id, scoped to its owning platform user")
            .WithDescription("Called by Wallet Service during delegation renewal. The platformUserId "
                + "query parameter scopes the lookup so cross-user device probing is impossible.")
            .RequireAuthorization("RequireService");

        // Feature 114 (US3): device revocation propagation from Wallet Service.
        // Used when the citizen revokes from the PWA — Wallet flips status-list +
        // broadcasts SignalR, then calls here so the Tenant row reflects the
        // revocation when the citizen views devices from the main UI.
        group.MapDelete("/platform-user-devices/{deviceId:guid}", RevokePlatformUserDevice)
            .WithName("RevokePlatformUserDevice")
            .WithSummary("Mark a citizen wallet device as revoked (Tenant row only — no further S2S)")
            .WithDescription("Called by Wallet Service after a PWA-initiated revoke. Idempotent on "
                + "already-revoked devices. Does NOT call back to Wallet — the caller has already "
                + "flipped the status-list bit, so a callback would loop. The platformUserId query "
                + "parameter scopes the lookup so cross-user revocation is impossible (404 on mismatch).")
            .RequireAuthorization("RequireService");

        // Feature 114 (US3 PR3): list devices for the wallet PWA. Wallet proxies
        // its public GET /api/v1/wallet/devices through here so it never reaches
        // into Tenant's PlatformUserDevices table directly.
        group.MapGet("/platform-user-devices", ListPlatformUserDevices)
            .WithName("ListPlatformUserDevices")
            .WithSummary("List a citizen's enrolled wallet devices")
            .WithDescription("Called by Wallet Service to back the PWA's device list. "
                + "Returns active and revoked devices ordered by enrolment desc, scoped "
                + "to the supplied platformUserId.")
            .RequireAuthorization("RequireService");

        // Feature 114 (US3 PR3): rename a device.
        group.MapPut("/platform-user-devices/{deviceId:guid}/label", UpdatePlatformUserDeviceLabel)
            .WithName("UpdatePlatformUserDeviceLabel")
            .WithSummary("Rename a citizen wallet device")
            .WithDescription("Called by Wallet Service when the citizen renames a device "
                + "from the PWA. Validates label length 1..120. 404 indistinguishable from "
                + "non-existence on cross-user mismatch.")
            .RequireAuthorization("RequireService");

        return app;
    }

    private static async Task<Results<Ok<PlatformUserDeviceRegistrationResponse>, BadRequest<string>>> RegisterPlatformUserDevice(
        [FromBody] PlatformUserDeviceRegistrationRequest request,
        IPlatformUserDeviceService deviceService,
        CancellationToken ct)
    {
        try
        {
            var device = await deviceService.RegisterAsync(
                request.PlatformUserId,
                request.Label,
                request.DevicePublicJwkThumbprint,
                request.DevicePublicJwkJson,
                request.Platform,
                request.UserAgent,
                request.DelegationExpiresAt,
                request.DelegationCredentialJti,
                request.StatusListId,
                request.StatusListIndex,
                ct);

            return TypedResults.Ok(new PlatformUserDeviceRegistrationResponse(
                device.Id, device.EnrolledAt));
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<Results<Ok<PlatformUserDeviceLookupResponse>, NotFound>> GetPlatformUserDevice(
        Guid deviceId,
        [FromQuery] Guid platformUserId,
        IPlatformUserDeviceService deviceService,
        CancellationToken ct)
    {
        var device = await deviceService.GetByIdAsync(deviceId, platformUserId, ct);
        if (device is null) return TypedResults.NotFound();

        return TypedResults.Ok(new PlatformUserDeviceLookupResponse(
            device.Id,
            device.PlatformUserId,
            device.Label,
            device.DevicePublicJwkThumbprint,
            device.DevicePublicJwkJson,
            device.Platform,
            device.Status.ToString(),
            device.EnrolledAt,
            device.DelegationExpiresAt,
            device.DelegationCredentialJti,
            device.StatusListId,
            device.StatusListIndex));
    }

    private static async Task<Results<NoContent, NotFound>> RevokePlatformUserDevice(
        Guid deviceId,
        [FromQuery] Guid platformUserId,
        IPlatformUserDeviceService deviceService,
        CancellationToken ct)
    {
        var revoked = await deviceService.RevokeAsync(deviceId, platformUserId, ct);
        return revoked is null ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Ok<PlatformUserDeviceListResponse>> ListPlatformUserDevices(
        [FromQuery] Guid platformUserId,
        IPlatformUserDeviceService deviceService,
        CancellationToken ct)
    {
        var devices = await deviceService.ListAsync(platformUserId, ct);
        var items = devices.Select(d => new PlatformUserDeviceLookupResponse(
            d.Id,
            d.PlatformUserId,
            d.Label,
            d.DevicePublicJwkThumbprint,
            d.DevicePublicJwkJson,
            d.Platform,
            d.Status.ToString(),
            d.EnrolledAt,
            d.DelegationExpiresAt,
            d.DelegationCredentialJti,
            d.StatusListId,
            d.StatusListIndex)).ToList();
        return TypedResults.Ok(new PlatformUserDeviceListResponse(items));
    }

    private static async Task<Results<NoContent, NotFound, BadRequest<string>>> UpdatePlatformUserDeviceLabel(
        Guid deviceId,
        [FromQuery] Guid platformUserId,
        [FromBody] PlatformUserDeviceLabelUpdateRequest request,
        IPlatformUserDeviceService deviceService,
        CancellationToken ct)
    {
        try
        {
            var updated = await deviceService.UpdateLabelAsync(deviceId, platformUserId, request.Label, ct);
            return updated is null ? TypedResults.NotFound() : TypedResults.NoContent();
        }
        catch (ArgumentException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    /// <summary>Internal response wrapping a list of devices.</summary>
    public sealed record PlatformUserDeviceListResponse(IReadOnlyList<PlatformUserDeviceLookupResponse> Devices);

    /// <summary>Internal request body for label updates.</summary>
    public sealed record PlatformUserDeviceLabelUpdateRequest(string Label);

    /// <summary>Internal response for a single device lookup.</summary>
    public sealed record PlatformUserDeviceLookupResponse(
        Guid DeviceId,
        Guid PlatformUserId,
        string Label,
        string DevicePublicJwkThumbprint,
        string DevicePublicJwkJson,
        string Platform,
        string Status,
        DateTimeOffset EnrolledAt,
        DateTimeOffset DelegationExpiresAt,
        string DelegationCredentialJti,
        int StatusListId,
        int StatusListIndex);

    /// <summary>Internal request body for citizen device registration.</summary>
    public sealed record PlatformUserDeviceRegistrationRequest(
        Guid PlatformUserId,
        string Label,
        string DevicePublicJwkThumbprint,
        string DevicePublicJwkJson,
        string Platform,
        string UserAgent,
        DateTimeOffset DelegationExpiresAt,
        string DelegationCredentialJti,
        int StatusListId,
        int StatusListIndex);

    /// <summary>Internal response with persisted device id and enrolment timestamp.</summary>
    public sealed record PlatformUserDeviceRegistrationResponse(
        Guid DeviceId,
        DateTimeOffset EnrolledAt);

    private static async Task<Results<Ok<DomainResolutionResponse>, NotFound>> ResolveDomain(
        string domain,
        ICustomDomainRepository domainRepository,
        IOrganizationRepository organizationRepository,
        CancellationToken cancellationToken)
    {
        var mapping = await domainRepository.GetByDomainAsync(domain, cancellationToken);

        if (mapping is null || mapping.Status != Models.CustomDomainStatus.Verified)
        {
            return TypedResults.NotFound();
        }

        var org = await organizationRepository.GetByIdAsync(mapping.OrganizationId, cancellationToken);
        if (org is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new DomainResolutionResponse(org.Subdomain));
    }

    internal record DomainResolutionResponse(string Subdomain);
}
