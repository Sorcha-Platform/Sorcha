// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Citizen wallet PWA endpoints (Feature 114). Mounted under <c>/api/v1/wallet/*</c>.
/// These are consumer-tier surfaces (spec 136): they require the JWT to carry the installation's
/// consumer audience (<c>{installation}:consumer</c>), enforced by the
/// <see cref="Microsoft.Extensions.Hosting.AuthorizationPolicies.RequireConsumerAudience"/> policy.
/// </summary>
public static class CitizenWalletEndpoints
{
    private const string CitizenWalletPolicyName = "CitizenWalletAudience";

    /// <summary>Maps the citizen wallet endpoints.</summary>
    public static IEndpointRouteBuilder MapCitizenWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallet")
            .WithTags("Citizen Wallet")
            // Spec 136: consumer-tier surface — only an installation consumer token ({install}:consumer)
            // is accepted; a platform or service token is refused at the audience layer (SC-002).
            .RequireAuthorization("RequireConsumerAudience")
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

        group.MapDelete("/devices/{deviceId:guid}", RevokeDevice)
            .WithName("RevokeCitizenDevice")
            .WithSummary("Revoke a citizen wallet device (PWA-initiated)")
            .WithDescription(
                "Looks up the device on the Tenant Service, flips the citizen-devices " +
                "status-list bit, broadcasts the SignalR DeviceRevoked event to the user's " +
                "group, and marks the Tenant row revoked via the existing service-to-service " +
                "channel. Returns 404 when the device does not exist or is not owned by the " +
                "caller (intentionally indistinguishable to avoid leaking device existence).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/devices", ListDevices)
            .WithName("ListCitizenDevices")
            .WithSummary("List the authenticated citizen's enrolled wallet devices")
            .WithDescription(
                "Mirror of GET /api/v1/me/devices for the wallet PWA. Proxies through " +
                "to the Tenant Service via service-to-service auth so the PWA only ever " +
                "talks to the Wallet Service for citizen-wallet operations.")
            .Produces<DeviceListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPut("/devices/{deviceId:guid}/label", UpdateDeviceLabel)
            .WithName("UpdateCitizenDeviceLabel")
            .WithSummary("Rename a citizen wallet device")
            .WithDescription(
                "Updates the citizen-visible device label (1..120 chars). 404 when " +
                "the device does not exist or is not owned by the caller.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/presentations/log", ReportPresentationLog)
            .WithName("ReportCitizenPresentationLog")
            .WithSummary("Report presentations the wallet has made (US5)")
            .WithDescription(
                "Accepts a batch of presentation-log entries the wallet recorded locally " +
                "and reports them to the platform so the citizen's cross-device history is " +
                "filled. Returns 202 Accepted immediately; dedupe (per entry id, 24h) " +
                "and forwarding happen off the request path. Wallets may re-report the same " +
                "entry safely — duplicates are absorbed by the entry-id dedupe.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/presentations", ListPresentations)
            .WithName("ListCitizenPresentations")
            .WithSummary("List the citizen's presentation history (US5)")
            .WithDescription(
                "Returns every presentation the authenticated citizen has reported, from " +
                "any of their devices, newest-first. Backs the PWA Activity page's " +
                "cross-device history. Returns an empty list (never 404) when there is no " +
                "history. Carries disclosed claim names only — never values.")
            .Produces<PresentationHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapDelete("/presentations/{id:guid}", DeletePresentation)
            .WithName("DeleteCitizenPresentation")
            .WithSummary("Delete a presentation from the citizen's history (US5)")
            .WithDescription(
                "Server-authoritative delete: removes the entry from the citizen's history " +
                "across all their devices. Idempotent. A delete targeting another citizen's " +
                "entry, or a non-existent entry, returns 204 — indistinguishable from success, " +
                "to avoid leaking existence. Does not affect the verifier's own records " +
                "(there is no register/ledger record for these presentations).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ListPresentations(
        HttpContext context,
        ICitizenPresentationStore store,
        CancellationToken ct)
    {
        var (platformUserId, _, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        var entries = await store.ListAsync(platformUserId.Value, ct);
        return Results.Ok(new PresentationHistoryResponse { Entries = entries });
    }

    private static async Task<IResult> DeletePresentation(
        Guid id,
        HttpContext context,
        ICitizenPresentationStore store,
        CancellationToken ct)
    {
        var (platformUserId, _, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        // Idempotent + cross-user-indistinguishable: always 204 regardless of whether
        // a row was removed. DeleteAsync is already scoped to the caller's platform user.
        await store.DeleteAsync(platformUserId.Value, id, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReportPresentationLog(
        [FromBody] PresentationLogReportRequest request,
        HttpContext context,
        IValidator<PresentationLogReportRequest> validator,
        IServiceScopeFactory scopeFactory,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var (platformUserId, _, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        var uid = platformUserId.Value;
        var entries = request.Entries;

        // Dispatch dedupe + forward off the request path so the wallet gets a fast
        // 202 and is not blocked on downstream forwarding. Own DI scope (the
        // reporter is scoped); CancellationToken.None so a client disconnect does
        // not abort the forward mid-batch.
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var reporter = scope.ServiceProvider.GetRequiredService<ICitizenPresentationLogReporter>();
            try
            {
                var accepted = await reporter.ReportAsync(uid, entries, CancellationToken.None);
                logger.LogInformation(
                    "Presentation-log report processed platformUser={PlatformUserId} reported={Reported} accepted={Accepted}",
                    uid, entries.Count, accepted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Presentation-log report failed platformUser={PlatformUserId} reported={Reported}",
                    uid, entries.Count);
            }
        });

        return Results.Accepted();
    }

    private static async Task<IResult> ListDevices(
        HttpContext context,
        IPlatformUserDeviceClient deviceClient,
        CancellationToken ct)
    {
        var (platformUserId, _, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        var devices = await deviceClient.ListAsync(platformUserId.Value, ct);
        var summaries = devices.Select(d => new DeviceSummary
        {
            DeviceId = d.DeviceId,
            Label = d.Label,
            Platform = d.Platform,
            Status = string.Equals(d.Status, "Revoked", StringComparison.OrdinalIgnoreCase)
                ? DeviceStatus.Revoked
                : DeviceStatus.Active,
            EnrolledAt = d.EnrolledAt,
            DelegationExpiresAt = d.DelegationExpiresAt
        }).ToList();

        return Results.Ok(new DeviceListResponse { Devices = summaries });
    }

    private static async Task<IResult> UpdateDeviceLabel(
        Guid deviceId,
        [FromBody] DeviceLabelUpdateRequest request,
        HttpContext context,
        IPlatformUserDeviceClient deviceClient,
        CancellationToken ct)
    {
        var (platformUserId, _, _) = ResolveCitizenContext(context.User);
        if (platformUserId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 120)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["label"] = ["Label must be 1..120 characters."]
            });
        }

        var ok = await deviceClient.UpdateLabelAsync(deviceId, platformUserId.Value, request.Label, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RevokeDevice(
        Guid deviceId,
        HttpContext context,
        IPlatformUserDeviceClient deviceClient,
        IDeviceRevocationService revocation,
        Sorcha.Wallet.Service.Services.Implementation.ICitizenDeviceInboxWriter inboxWriter,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var (platformUserId, _, organizationId) = ResolveCitizenContext(context.User);
        if (platformUserId is null || organizationId is null)
        {
            return Results.Unauthorized();
        }

        var device = await deviceClient.GetByIdAsync(deviceId, platformUserId.Value, ct);
        if (device is null)
        {
            return Results.NotFound();
        }

        await revocation.RevokeAsync(
            organizationId.Value,
            device.StatusListId,
            device.StatusListIndex,
            deviceId,
            platformUserId.Value,
            ct);

        var tenantOk = await deviceClient.RevokeAsync(deviceId, platformUserId.Value, ct);
        if (!tenantOk)
        {
            // Wallet side already revoked; Tenant lookup said the device exists but
            // the revoke endpoint reported 404. Possible race (concurrent revoke
            // from the web UI) — log and report success since the desired end-state
            // is achieved.
            logger.LogWarning(
                "PWA-initiated revoke: Tenant returned 404 on RevokeAsync for deviceId={DeviceId} " +
                "(platformUser={PlatformUserId}) after a successful GetByIdAsync — concurrent revoke?",
                deviceId, platformUserId);
        }

        // Phase 2c of the Snackbar retirement — drop a durable Category=Security
        // inbox entry on the citizen. Fail-safe: writer catches transport errors.
        // Idempotent on (platformUserId, deviceId) so concurrent revokes from
        // web + PWA produce a single inbox entry.
        await inboxWriter.WriteDeviceRevokedAsync(
            platformUserId: platformUserId.Value,
            deviceId: deviceId,
            deviceLabel: device.Label,
            ct: ct).ConfigureAwait(false);

        return Results.NoContent();
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

    /// <summary>SignalR client method name for the device-enrolled broadcast (Feature 128).</summary>
    public const string DeviceEnrolledEvent = "DeviceEnrolled";

    private static async Task<IResult> EnrolDevice(
        [FromBody] DeviceEnrolmentRequest request,
        HttpContext context,
        IValidator<DeviceEnrolmentRequest> validator,
        IHolderKeyService holderKeys,
        IDeviceDelegationIssuer issuer,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        IPlatformUserDeviceClient deviceClient,
        IHolderAddressLookup holderAddressLookup,
        Microsoft.AspNetCore.SignalR.IHubContext<Sorcha.Wallet.Service.Hubs.WalletHub> hub,
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

        // Feature 114 US4 — pin the citizen holder wallet address ↔ PlatformUserId
        // mapping the moment both are available together (the citizen's JWT carries
        // both at enrolment time but neither flows to InboundCredentialDetector).
        // Idempotent on retry.
        await holderAddressLookup.RegisterAsync(citizenWallet, platformUserId.Value, ct);

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

            // Feature 128 FR-014 — broadcast device-enrolled on the citizen's
            // hub group so any PWA instance the citizen has open (e.g. an
            // unpaired sibling tab in the takeover state) dismisses its
            // takeover without waiting on the next natural probe refresh.
            // Failure here is non-fatal — the registration succeeded; missing
            // the push only widens the dismissal window.
            try
            {
                var group = Sorcha.Wallet.Service.Hubs.WalletHubGroups.CitizenWallet(platformUserId.Value);
                await hub.Clients.Group(group).SendAsync(DeviceEnrolledEvent, registered.DeviceId, ct);
            }
            catch (Exception hubEx)
            {
                logger.LogWarning(hubEx,
                    "DeviceEnrolled hub broadcast failed for platformUser={PlatformUserId} deviceId={DeviceId} — registration succeeded",
                    platformUserId, registered.DeviceId);
            }

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
