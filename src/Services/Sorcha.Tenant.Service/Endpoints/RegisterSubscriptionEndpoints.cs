// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for managing organization register subscriptions.
/// Allows organizations to subscribe/unsubscribe from registers and list their subscriptions.
/// </summary>
public static class RegisterSubscriptionEndpoints
{
    /// <summary>
    /// Maps register subscription endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapRegisterSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        // Org-scoped endpoints requiring at least Member role for reads, Admin for writes
        var orgGroup = app.MapGroup("/api/organizations/{orgId:guid}/register-subscriptions")
            .WithTags("Register Subscriptions");

        orgGroup.MapGet("/", ListSubscriptions)
            .WithName("ListRegisterSubscriptions")
            .WithSummary("List register subscriptions for an organization")
            .WithDescription("Returns a paginated list of register subscriptions for the specified organization. "
                + "Supports page and pageSize query parameters (defaults: page=1, pageSize=25).")
            .Produces<RegisterSubscriptionListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        orgGroup.MapGet("/{registerId}", GetSubscription)
            .WithName("GetRegisterSubscription")
            .WithSummary("Get a specific register subscription")
            .WithDescription("Returns the subscription details for a specific register within the organization. "
                + "Returns 404 if the organization does not have a subscription to the specified register.")
            .Produces<RegisterSubscriptionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        orgGroup.MapPost("/", Subscribe)
            .WithName("SubscribeToRegister")
            .WithSummary("Subscribe organization to a register")
            .WithDescription("Creates a new subscription for the organization to a public register. "
                + "Requires a valid 32-character hex register ID. Returns 409 if already subscribed.")
            .Produces<RegisterSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization("RequireAdministrator");

        orgGroup.MapDelete("/{registerId}", Unsubscribe)
            .WithName("UnsubscribeFromRegister")
            .WithSummary("Unsubscribe organization from a register")
            .WithDescription("Revokes the organization's subscription to a register. "
                + "Owner subscriptions cannot be revoked (returns 400). Returns 204 on success.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization("RequireAdministrator");

        // User-scoped endpoint for listing subscribed registers via JWT claims
        app.MapGet("/api/me/subscribed-registers", GetMySubscribedRegisters)
            .WithName("GetMySubscribedRegisters")
            .WithSummary("List registers the current user's organization is subscribed to")
            .WithDescription("Resolves the organization from the authenticated user's org_id JWT claim "
                + "and returns all active register subscriptions for that organization.")
            .WithTags("Register Subscriptions")
            .Produces<IReadOnlyList<RegisterSubscriptionResponse>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// GET /api/organizations/{orgId}/register-subscriptions — paginated list of subscriptions.
    /// </summary>
    private static async Task<IResult> ListSubscriptions(
        Guid orgId,
        IRegisterSubscriptionService subscriptionService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var result = await subscriptionService.ListAsync(orgId, page, pageSize, cancellationToken);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// GET /api/organizations/{orgId}/register-subscriptions/{registerId} — single subscription.
    /// </summary>
    private static async Task<IResult> GetSubscription(
        Guid orgId,
        string registerId,
        IRegisterSubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.GetAsync(orgId, registerId, cancellationToken);
        return subscription is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(subscription);
    }

    /// <summary>
    /// POST /api/organizations/{orgId}/register-subscriptions — subscribe to a register.
    /// </summary>
    private static async Task<IResult> Subscribe(
        Guid orgId,
        SubscribeRequest request,
        IRegisterSubscriptionService subscriptionService,
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
            var isOwner = string.Equals(request.SubscriptionType, "Owner", StringComparison.OrdinalIgnoreCase);

            var subscription = isOwner
                ? await subscriptionService.CreateOwnerSubscriptionAsync(
                    orgId, request.RegisterId, request.RegisterName, userId, cancellationToken)
                : await subscriptionService.SubscribeAsync(
                    orgId, request.RegisterId, request.RegisterName, userId, request.Description, cancellationToken);

            return TypedResults.Created(
                $"/api/organizations/{orgId}/register-subscriptions/{request.RegisterId}",
                subscription);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already has a subscription"))
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Duplicate Subscription",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "registerId"] = [ex.Message]
            });
        }
    }

    /// <summary>
    /// DELETE /api/organizations/{orgId}/register-subscriptions/{registerId} — unsubscribe.
    /// </summary>
    private static async Task<IResult> Unsubscribe(
        Guid orgId,
        string registerId,
        IRegisterSubscriptionService subscriptionService,
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
            await subscriptionService.UnsubscribeAsync(orgId, registerId, userId, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Owner"))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid Operation",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    /// <summary>
    /// GET /api/me/subscribed-registers — returns active subscriptions for the user's org.
    /// </summary>
    private static async Task<IResult> GetMySubscribedRegisters(
        IRegisterSubscriptionService subscriptionService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var orgId = GetOrgId(user);
        if (orgId == Guid.Empty)
        {
            return TypedResults.Unauthorized();
        }

        var subscriptions = await subscriptionService.GetSubscribedRegistersForOrgAsync(orgId, cancellationToken);
        return TypedResults.Ok(subscriptions);
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private static Guid GetOrgId(ClaimsPrincipal user)
    {
        var orgIdClaim = user.FindFirst("org_id")?.Value
            ?? user.FindFirst("organization_id")?.Value;
        return Guid.TryParse(orgIdClaim, out var orgId) ? orgId : Guid.Empty;
    }
}
