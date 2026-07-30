// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

using Sorcha.Tenant.Service.Authorization;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for managing private register invitations.
/// Allows register owners to invite organizations and target organizations to accept invitations.
/// </summary>
public static class RegisterInvitationEndpoints
{
    /// <summary>
    /// Maps register invitation endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapRegisterInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{orgId:guid}/register-invitations")
            .WithTags("Register Invitations")
            // B2+ wave 2 (2026-07-29 review): nothing compared the caller to {orgId}. Safe for the
            // accept route too — AcceptInvitation passes the ROUTE orgId to
            // IRegisterInvitationService.AcceptAsync as the ACCEPTING organisation, so the caller
            // must be a member of it; binding them is the intended semantics, not a restriction.
            .RequireCallerOrganization();

        group.MapPost("/", CreateInvitation)
            .WithName("CreateRegisterInvitation")
            .WithSummary("Create a private register invitation")
            .WithDescription("Creates a signed and encrypted invitation token for a target organization "
                + "to join a private register owned by this organization. The token is returned for "
                + "out-of-band delivery (email, messaging, etc.).")
            .Produces<InvitationCreatedResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

        group.MapPost("/accept", AcceptInvitation)
            .WithName("AcceptRegisterInvitation")
            .WithSummary("Accept a register invitation")
            .WithDescription("Accepts an invitation token: decrypts, verifies the source signature, "
                + "checks nonce replay protection, validates expiry and target organization, "
                + "then creates an Invited subscription to the register.")
            .Produces<InvitationAcceptedResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

        group.MapGet("/", ListInvitations)
            .WithName("ListRegisterInvitations")
            .WithSummary("List register invitations")
            .WithDescription("Lists invitations for this organization, optionally filtered by direction "
                + "(sent, received, or all).")
            .Produces<InvitationListResponse>()
            .RequireAuthorization();

        group.MapDelete("/{invitationId}", RevokeInvitation)
            .WithName("RevokeRegisterInvitation")
            .WithSummary("Revoke a pending invitation")
            .WithDescription("Revokes a pending invitation created by this organization. "
                + "Already-accepted invitations cannot be revoked.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

        return app;
    }

    private static async Task<IResult> CreateInvitation(
        Guid orgId,
        [FromBody] CreateRegisterInvitationRequest request,
        [FromServices] IRegisterInvitationService invitationService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryGetUserId(user, out var userId))
            return Results.Unauthorized();

        try
        {
            var result = await invitationService.CreateAsync(orgId, request, userId, ct);
            return Results.Created($"/api/organizations/{orgId}/register-invitations/{result.InvitationId}", result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> AcceptInvitation(
        Guid orgId,
        [FromBody] AcceptInvitationRequest request,
        [FromServices] IRegisterInvitationService invitationService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!TryGetUserId(user, out var userId))
            return Results.Unauthorized();

        try
        {
            var result = await invitationService.AcceptAsync(orgId, request, userId, ct);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already subscribed"))
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListInvitations(
        Guid orgId,
        [FromQuery] string? direction,
        [FromServices] IRegisterInvitationService invitationService,
        CancellationToken ct)
    {
        try
        {
            var result = await invitationService.ListAsync(orgId, direction ?? "all", ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RevokeInvitation(
        Guid orgId,
        string invitationId,
        [FromServices] IRegisterInvitationService invitationService,
        CancellationToken ct)
    {
        try
        {
            await invitationService.RevokeAsync(orgId, invitationId, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
