// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Endpoints for managing domain restrictions on auto-provisioning.
/// Admins can restrict which email domains are allowed for OIDC auto-provisioning.
/// </summary>
public static class DomainRestrictionEndpoints
{
    /// <summary>
    /// Maps domain restriction endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapDomainRestrictionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/domain-restrictions")
            .WithTags("Domain Restrictions")
            .RequireAuthorization("RequireAdministrator");

        group.MapGet("/", GetDomainRestrictions)
            .WithName("GetDomainRestrictions")
            .WithSummary("Get domain restrictions")
            .WithDescription("Returns the allowed email domains for auto-provisioning and whether restrictions are active.")
            .Produces<DomainRestrictionsResponse>()
            .Produces(404);

        group.MapPut("/", UpdateDomainRestrictions)
            .WithName("UpdateDomainRestrictions")
            .WithSummary("Update domain restrictions")
            .WithDescription("Sets the allowed email domains for auto-provisioning. An empty array disables restrictions (all domains allowed). Invited users always bypass domain restrictions.")
            .Produces<DomainRestrictionsResponse>()
            .Produces(404)
            .ProducesValidationProblem();

        return app;
    }

    /// <summary>
    /// GET /api/organizations/{organizationId}/domain-restrictions — returns current domain restrictions.
    /// </summary>
    private static async Task<IResult> GetDomainRestrictions(
        Guid organizationId,
        IOrganizationService organizationService,
        CancellationToken cancellationToken)
    {
        var result = await organizationService.GetDomainRestrictionsAsync(organizationId, cancellationToken);

        if (result is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// PUT /api/organizations/{organizationId}/domain-restrictions — updates allowed email domains.
    /// </summary>
    private static async Task<IResult> UpdateDomainRestrictions(
        Guid organizationId,
        UpdateDomainRestrictionsRequest request,
        IOrganizationService organizationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Validate domain formats
        var invalidDomains = request.AllowedDomains
            .Where(d => !string.IsNullOrWhiteSpace(d) && !IsValidDomainFormat(d.Trim()))
            .ToArray();

        if (invalidDomains.Length > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["allowedDomains"] = [$"Invalid domain format: {string.Join(", ", invalidDomains)}. Domains must be in format like 'example.com'."]
            });
        }

        var userId = httpContext.User.FindFirst("sub")?.Value;
        var userGuid = Guid.TryParse(userId, out var uid) ? uid : Guid.Empty;

        var result = await organizationService.UpdateDomainRestrictionsAsync(
            organizationId, request.AllowedDomains, userGuid, cancellationToken);

        if (result is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Validates that a string looks like a valid domain (e.g., "acme.com", "corp.example.org").
    /// </summary>
    private static bool IsValidDomainFormat(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253)
            return false;

        // Simple domain validation: at least one dot, valid chars
        var parts = domain.Split('.');
        if (parts.Length < 2)
            return false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || part.Length > 63)
                return false;

            // Each label must start/end with alphanumeric, can contain hyphens
            if (!char.IsLetterOrDigit(part[0]) || !char.IsLetterOrDigit(part[^1]))
                return false;

            if (!part.All(c => char.IsLetterOrDigit(c) || c == '-'))
                return false;
        }

        return true;
    }
}
