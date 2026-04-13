// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sorcha.AddressLookup;
using Sorcha.ServiceDefaults;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// HTTP endpoints for the Sorcha address lookup service. Contract:
/// <c>specs/103-verified-citizen-v2/contracts/address-lookup-api.yaml</c>.
/// </summary>
/// <remarks>
/// Auth-gated to any authenticated user (callers are public-org users filling
/// in a form, so they already hold a JWT by the time they reach the endpoint).
/// Rate-limited via the standard API policy. Never throws on upstream
/// failure — all provider errors become graceful-degradation results the form
/// renderer can handle cleanly.
/// </remarks>
public static class AddressLookupEndpoints
{
    /// <summary>Maps the address lookup endpoints into the application.</summary>
    public static IEndpointRouteBuilder MapAddressLookupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/address-lookup")
            .WithTags("AddressLookup")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapPost("/postcode", LookupPostcode)
            .WithName("LookupPostcode")
            .WithSummary("Look up a postcode and return validation metadata or full-address candidates")
            .WithDescription(
                "Resolves the supplied postcode against the most capable available provider for " +
                "the target country. Returns either a ValidateOnly result (postcode metadata + " +
                "town/region) or a FullAddress result (list of address candidates). On provider " +
                "unavailability or unknown postcode, returns 200 with IsValid=false and Provider='none' " +
                "so the form renderer can fall back gracefully to plain text entry.")
            .Produces<AddressLookupResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/providers", ListProviders)
            .WithName("ListAddressLookupProviders")
            .WithSummary("List configured address lookup providers and their capabilities")
            .WithDescription(
                "Returns the set of providers currently registered in this deployment along with " +
                "their capability (ValidateOnly | FullAddress), supported country codes, and " +
                "current availability. Used by the form renderer to decide which UI to show for " +
                "fields that declare x-address-lookup.")
            .Produces<IReadOnlyList<AddressLookupProviderInfo>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>Request body for <c>POST /api/address-lookup/postcode</c>.</summary>
    public sealed class PostcodeLookupRequest
    {
        /// <summary>The postcode to look up. Whitespace and case are normalised by the platform.</summary>
        [Required]
        [StringLength(12, MinimumLength = 3)]
        public string Postcode { get; set; } = string.Empty;

        /// <summary>Optional ISO 3166-1 alpha-2 country code. Defaults to GB when omitted.</summary>
        [StringLength(2, MinimumLength = 2)]
        [RegularExpression("^[A-Z]{2}$")]
        public string? CountryHint { get; set; }
    }

    private static async Task<IResult> LookupPostcode(
        [FromBody] PostcodeLookupRequest request,
        AddressLookupService lookupService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Postcode))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["postcode"] = ["Postcode is required."]
            });
        }

        var result = await lookupService.LookupAsync(request.Postcode, request.CountryHint, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListProviders(
        AddressLookupService lookupService,
        CancellationToken cancellationToken)
    {
        var providers = await lookupService.ListProvidersAsync(cancellationToken);
        return Results.Ok(providers);
    }
}
