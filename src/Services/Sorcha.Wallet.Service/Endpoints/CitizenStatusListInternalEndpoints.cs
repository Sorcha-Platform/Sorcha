// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Internal service-to-service endpoints for citizen device revocation
/// (Feature 114, US3). Called by Tenant Service after a citizen revokes
/// from the main UI's <c>DELETE /api/v1/me/devices/{id}</c> flow — Tenant
/// flips its local row then calls here to flip the status-list bit and
/// broadcast the SignalR <c>DeviceRevoked</c> event.
/// </summary>
public static class CitizenStatusListInternalEndpoints
{
    /// <summary>Maps the internal citizen status list endpoints.</summary>
    public static IEndpointRouteBuilder MapCitizenStatusListInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/internal/citizen-status-list")
            .WithTags("Internal")
            .ExcludeFromDescription();

        group.MapPost("/revoke", Revoke)
            .WithName("RevokeCitizenDeviceStatusListBit")
            .WithSummary("Flip citizen-devices status-list bit + broadcast DeviceRevoked")
            .WithDescription(
                "Called by Tenant Service after a web-UI revoke. Pure Wallet-side: " +
                "FlipAsync on the publisher (idempotent on already-set bits) + a " +
                "SignalR DeviceRevoked broadcast on the citizen's group. Does NOT " +
                "call back to Tenant — the caller already flipped the Tenant row.")
            .RequireAuthorization(AuthorizationPolicies.RequireService);

        return app;
    }

    private static async Task<NoContent> Revoke(
        [FromBody] RevokeRequest request,
        IDeviceRevocationService revocation,
        CancellationToken ct)
    {
        await revocation.RevokeAsync(
            request.OrganizationId,
            request.ListId,
            request.IndexInList,
            request.DeviceId,
            request.PlatformUserId,
            ct);
        return TypedResults.NoContent();
    }

    /// <summary>Internal request body for the citizen-device status-list revoke flip.</summary>
    public sealed record RevokeRequest(
        Guid OrganizationId,
        int ListId,
        int IndexInList,
        Guid DeviceId,
        Guid PlatformUserId);
}
