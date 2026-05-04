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
