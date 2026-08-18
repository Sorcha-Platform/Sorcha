// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        // Feature 149: resolve an org's canonical operational wallet address (A) so the
        // Wallet Service can anchor the issuer DID on did:sorcha:org:{A}.
        group.MapGet("/orgs/{orgId:guid}/wallet-address", ResolveOrgWalletAddress)
            .WithName("ResolveOrgWalletAddress")
            .WithSummary("Resolve an organisation's canonical operational wallet address")
            .WithDescription("Returns Organization.WalletAddress — the canonical did:sorcha:org anchor. "
                + "404 when the org is unknown OR has no provisioned wallet; the caller treats 404 as "
                + "'no resolvable issuer identity' and fails credential issuance closed.")
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

        // Feature 118 (US3 follow-up): resolve UserIdentity.Id → PlatformUser.Id.
        // Used by inbox writers in other services (Blueprint, Wallet) that have a
        // ParticipantInfo (which carries the org-scoped UserIdentity id) and need
        // the cross-org PlatformUserId to address the inbox row.
        group.MapGet("/users/by-identity/{userIdentityId:guid}", ResolvePlatformUserByIdentity)
            .WithName("ResolvePlatformUserByIdentity")
            .WithSummary("Resolve UserIdentity.Id to its owning PlatformUser.Id")
            .WithDescription("Returns the cross-org PlatformUserId that owns the supplied UserIdentity. "
                + "Used by inbox writers in other services that resolve participant by wallet but only "
                + "see the org-scoped identity id in ParticipantInfo. 404 if the UserIdentity does not exist.")
            .RequireAuthorization("RequireService");

        // Issue #1264: the live-claims read. A JWT claim is a snapshot from mint time, so any
        // decision that must reflect reality at the moment it is made has to read server state
        // instead. Deliberately ONE batch route keyed by claim name — not an endpoint per checkable
        // attribute — so a caller resolving several bindings makes one round trip and a newly
        // resolvable attribute is a mapping entry in the service, never a new route.
        group.MapGet("/platform-users/{platformUserId:guid}/exists", PlatformUserExists)
            .WithName("PlatformUserExists")
            .WithSummary("Whether the supplied id names a real platform user.")
            .WithDescription("Issue #1506. InboxEntries.PlatformUserId is a foreign key, so a caller "
                + "writing on someone's behalf needs to tell 'this is a platform user' from 'this is "
                + "some other GUID' BEFORE writing — every id here is a GUID, so a wrong one is "
                + "indistinguishable by shape and only the database notices. Deliberately returns a "
                + "bare boolean rather than the user, so it discloses nothing but existence.")
            .Produces<PlatformUserExistsResponse>(StatusCodes.Status200OK);

        group.MapGet("/platform-users/{platformUserId:guid}/claims", ResolveLivePlatformUserClaims)
            .WithName("ResolveLivePlatformUserClaims")
            .WithSummary("Resolve the current value of named identity claims for a platform user")
            .WithDescription("Reads the requested claim names from live server state rather than from a "
                + "previously-minted token. Names are the JWT claim vocabulary (e.g. email_verified). "
                + "Unsupported names are omitted from the response so the caller fails closed on them "
                + "visibly. 404 when the platform user does not exist — distinguishable from a user whose "
                + "email is simply unverified, because the two demand different handling.")
            .RequireAuthorization("RequireService");

        return app;
    }

    /// <summary>
    /// Resolves live identity claim values for a platform user. See
    /// <see cref="ILivePlatformUserClaimsService"/> for why this exists and why it is a batch read.
    /// </summary>
    private static async Task<Ok<PlatformUserExistsResponse>> PlatformUserExists(
        Guid platformUserId,
        Sorcha.Tenant.Service.Data.TenantDbContext db,
        CancellationToken ct)
    {
        // 200 with exists:false rather than 404 — the caller is asking a question, and "no" is a
        // successful answer to it. A 404 would be ambiguous with the route being absent, which is
        // exactly the confusion that makes a missing internal endpoint hard to diagnose.
        var exists = await db.PlatformUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == platformUserId, ct);

        return TypedResults.Ok(new PlatformUserExistsResponse(platformUserId, exists));
    }

    private static async Task<Results<Ok<IReadOnlyDictionary<string, string>>, NotFound, BadRequest<string>>>
        ResolveLivePlatformUserClaims(
            Guid platformUserId,
            [FromQuery] string? names,
            ILivePlatformUserClaimsService claims,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(names))
        {
            return TypedResults.BadRequest(
                "At least one claim name is required (comma-separated 'names' query parameter). "
                + "This endpoint never returns a bulk dump — the request must state its intent.");
        }

        var requested = names
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
        {
            return TypedResults.BadRequest("At least one claim name is required.");
        }

        var resolved = await claims.ResolveAsync(platformUserId, requested, ct);
        return resolved is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(resolved);
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

    /// <summary>Wire shape for <see cref="ResolveOrgWalletAddress"/> (Feature 149).</summary>
    public sealed record OrgWalletAddressResponse(string WalletAddress);

    private static async Task<Results<Ok<OrgWalletAddressResponse>, NotFound>> ResolveOrgWalletAddress(
        Guid orgId,
        IOrganizationRepository organizationRepository,
        CancellationToken cancellationToken)
    {
        var org = await organizationRepository.GetByIdAsync(orgId, cancellationToken);
        if (org is null || string.IsNullOrWhiteSpace(org.WalletAddress))
        {
            // 404 covers both "unknown org" and "no provisioned wallet" — the caller
            // fails issuance closed rather than minting an unverifiable credential.
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new OrgWalletAddressResponse(org.WalletAddress));
    }

    /// <summary>Wire shape for <see cref="ResolvePlatformUserByIdentity"/>.</summary>
    public sealed record PlatformUserResolution(Guid UserIdentityId, Guid PlatformUserId);

    /// <summary>Whether an id names a real platform user (issue #1506).</summary>
    public sealed record PlatformUserExistsResponse(Guid PlatformUserId, bool Exists);

    private static async Task<Results<Ok<PlatformUserResolution>, NotFound>> ResolvePlatformUserByIdentity(
        Guid userIdentityId,
        Sorcha.Tenant.Service.Data.TenantDbContext db,
        CancellationToken ct)
    {
        // Single Postgres lookup against the public schema. UserIdentity.PlatformUserId
        // is the foreign key to the cross-org PlatformUser table — exactly what the
        // inbox addressing surface needs.
        var platformUserId = await db.UserIdentities
            .Where(u => u.Id == userIdentityId)
            .Select(u => u.PlatformUserId)
            .FirstOrDefaultAsync(ct);

        if (platformUserId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new PlatformUserResolution(userIdentityId, platformUserId));
    }
}
