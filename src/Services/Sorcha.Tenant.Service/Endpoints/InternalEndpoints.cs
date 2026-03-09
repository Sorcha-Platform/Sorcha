// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;

using Sorcha.Tenant.Service.Data.Repositories;

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
                + "Used internally by the API Gateway for domain-based routing.");

        return app;
    }

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
