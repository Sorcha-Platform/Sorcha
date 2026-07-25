// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Organization invitation management API endpoints.
/// </summary>
public static class InvitationEndpoints
{
    /// <summary>
    /// Maps invitation management endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/invitations")
            .WithTags("Invitations")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

        group.MapPost("/", CreateInvitation)
            .WithName("CreateInvitation")
            .WithSummary("Send an organization invitation")
            .WithDescription("Creates and sends an invitation email to join the organization with a specified role. Generates a 32-byte cryptographic token with configurable expiry (1-30 days, default 7).")
            .Produces<OrgInvitationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/", ListInvitations)
            .WithName("ListInvitations")
            .WithSummary("List organization invitations")
            .WithDescription("Lists all invitations for the organization, optionally filtered by status (Pending, Accepted, Expired, Revoked).")
            .Produces<List<OrgInvitationResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // Issue #1256 — the endpoint the admin UI's Resend button always needed. Rate-limited because
        // this is an outbound-email trigger reachable by any org administrator; Strict is the policy
        // CLAUDE.md pattern #8 reserves for exactly this shape.
        group.MapPost("/{invitationId:guid}/resend", ResendInvitation)
            .WithName("ResendInvitation")
            .WithSummary("Resend a pending invitation")
            .WithDescription("Re-sends the invitation email for a Pending invitation, ROTATING its token and "
                + "resetting its expiry to the original validity window. Any link already in flight stops "
                + "working — the fresh email supersedes it. Only Pending invitations can be resent (400 "
                + "otherwise, matching RevokeInvitation).")
            .RequireRateLimiting(RateLimitPolicies.Strict)
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/{invitationId:guid}/revoke", RevokeInvitation)
            .WithName("RevokeInvitation")
            .WithSummary("Revoke an invitation")
            .WithDescription("Revokes a pending invitation. Only Pending invitations can be revoked.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Created<OrgInvitationResponse>, Conflict<ProblemDetails>, ValidationProblem>> CreateInvitation(
        Guid organizationId,
        CreateOrgInvitationRequest request,
        IInvitationService invitationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        if (userId == Guid.Empty)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["user"] = ["User ID not found in claims"]
            });
        }

        try
        {
            var response = await invitationService.CreateInvitationAsync(
                organizationId, request, userId, cancellationToken);

            return TypedResults.Created(
                $"/api/organizations/{organizationId}/invitations/{response.Id}",
                response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Duplicate Invitation",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
    }

    private static async Task<Ok<List<OrgInvitationResponse>>> ListInvitations(
        Guid organizationId,
        IInvitationService invitationService,
        [FromQuery] InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var invitations = await invitationService.ListInvitationsAsync(
            organizationId, status, cancellationToken);
        return TypedResults.Ok(invitations);
    }

    /// <summary>
    /// POST /api/organizations/{organizationId}/invitations/{invitationId}/resend — re-send a pending
    /// invitation's email with a rotated token.
    /// </summary>
    /// <remarks>
    /// Issue #1256: the UI's Resend button used to POST here against a route that did not exist. The
    /// call 404'd, the page discarded the returned <c>false</c>, and the operator was told
    /// "Invitation resent" every time. Returns 400 (not 409) for a non-Pending invitation, matching
    /// <see cref="RevokeInvitation"/> — the issue text says 409, but the sibling endpoint's actual
    /// shape is 400 and an inconsistent pair is worse than either choice.
    /// </remarks>
    private static async Task<Results<Ok, BadRequest<ProblemDetails>, NotFound>> ResendInvitation(
        Guid organizationId,
        Guid invitationId,
        IInvitationService invitationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        try
        {
            var success = await invitationService.ResendInvitationAsync(
                organizationId, invitationId, userId, cancellationToken);

            return success
                ? TypedResults.Ok()
                : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid Operation",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    private static async Task<Results<Ok, BadRequest<ProblemDetails>, NotFound>> RevokeInvitation(
        Guid organizationId,
        Guid invitationId,
        IInvitationService invitationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(user);
        try
        {
            var success = await invitationService.RevokeInvitationAsync(
                organizationId, invitationId, userId, cancellationToken);

            return success
                ? TypedResults.Ok()
                : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid Operation",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
