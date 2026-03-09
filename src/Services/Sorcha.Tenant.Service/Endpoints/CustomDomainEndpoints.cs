// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;

using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Custom domain management API endpoints.
/// </summary>
public static class CustomDomainEndpoints
{
    /// <summary>
    /// Maps custom domain endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapCustomDomainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/custom-domain")
            .WithTags("Custom Domain")
            .RequireAuthorization("RequireAdministrator");

        group.MapGet("/", GetCustomDomain)
            .WithName("GetCustomDomain")
            .WithSummary("Get custom domain configuration")
            .WithDescription("Returns the custom domain setup and verification status for the organization.")
            .Produces<CustomDomainResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/", ConfigureCustomDomain)
            .WithName("ConfigureCustomDomain")
            .WithSummary("Configure custom domain")
            .WithDescription("Sets or updates the custom domain. Returns CNAME instructions for DNS configuration.")
            .Produces<CnameInstructionsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/", RemoveCustomDomain)
            .WithName("RemoveCustomDomain")
            .WithSummary("Remove custom domain")
            .WithDescription("Removes the custom domain configuration for the organization.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/verify", VerifyCustomDomain)
            .WithName("VerifyCustomDomain")
            .WithSummary("Verify custom domain CNAME")
            .WithDescription("Checks DNS resolution for the configured custom domain and updates verification status.")
            .Produces<DomainVerificationResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<CustomDomainResponse>> GetCustomDomain(
        Guid organizationId,
        ICustomDomainService domainService,
        CancellationToken cancellationToken)
    {
        var result = await domainService.GetCustomDomainAsync(organizationId, cancellationToken);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CnameInstructionsResponse>, Conflict<string>, BadRequest<string>>> ConfigureCustomDomain(
        Guid organizationId,
        ConfigureCustomDomainRequest request,
        ICustomDomainService domainService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub")!);

        try
        {
            var result = await domainService.ConfigureCustomDomainAsync(organizationId, request, userId, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already configured"))
        {
            return TypedResults.Conflict(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static async Task<NoContent> RemoveCustomDomain(
        Guid organizationId,
        ICustomDomainService domainService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub")!);
        await domainService.RemoveCustomDomainAsync(organizationId, userId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<DomainVerificationResponse>, BadRequest<string>>> VerifyCustomDomain(
        Guid organizationId,
        ICustomDomainService domainService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub")!);

        try
        {
            var result = await domainService.VerifyCustomDomainAsync(organizationId, userId, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }
}
