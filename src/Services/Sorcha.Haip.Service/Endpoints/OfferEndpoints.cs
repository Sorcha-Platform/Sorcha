// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sorcha.Haip.Service.Services;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// Internal endpoints for credential offer management. Called by the Blueprint Service
/// when a credential issuance action targets an external HAIP wallet.
/// </summary>
public static class OfferEndpoints
{
    /// <summary>
    /// Maps the HAIP credential offer management endpoints.
    /// </summary>
    public static void MapOfferEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/offers")
            .WithTags("HAIP Offers (Internal)");

        group.MapPost("/", CreateOffer)
            .WithName("CreateCredentialOffer")
            .WithSummary("Create a Credential Offer (service-to-service)")
            .WithDescription(
                "Creates a Credential Offer with a pre-authorized code for an external HAIP wallet. " +
                "Returns the offer details and a URI for QR code rendering.")
            .Produces<object>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithRequestValidation() // VAL-001 (#327): DataAnnotations on CreateOfferRequest → 400 ValidationProblem
            .RequireAuthorization(AuthorizationPolicies.RequireService); // SEC-013

        group.MapGet("/{offerId:guid}", GetOfferStatus)
            .WithName("GetOfferStatus")
            .WithSummary("Get Credential Offer status (service-to-service)")
            .WithDescription(
                "Returns the current lifecycle state of a Credential Offer (Pending, Exchanged, Expired). " +
                "The Blueprint Service polls this to drive the issuance workflow forward — for example, " +
                "advancing a workflow step once the wallet has redeemed the pre-authorized code.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthorizationPolicies.RequireService); // SEC-013
    }

    private static async Task<IResult> CreateOffer(
        [FromBody] CreateOfferRequest request,
        CredentialOfferService offerService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IssuerWalletAddress))
            return Results.BadRequest(new { error = "IssuerWalletAddress is required" });
        if (string.IsNullOrWhiteSpace(request.TenantId))
            return Results.BadRequest(new { error = "TenantId is required" });
        if (string.IsNullOrWhiteSpace(request.CredentialType))
            return Results.BadRequest(new { error = "CredentialType is required" });

        var (offer, offerUri) = await offerService.CreateOfferAsync(
            request.IssuerWalletAddress,
            request.TenantId,
            request.CredentialType,
            request.Claims ?? new(),
            request.DisclosablePaths,
            ct);

        return Results.Created($"/api/v1/offers/{offer.Id}", new
        {
            offerId = offer.Id,
            credentialOfferUri = offerUri,
            preAuthorizedCode = offer.PreAuthorizedCode,
            expiresAt = offer.ExpiresAt,
            status = offer.Status.ToString()
        });
    }

    private static IResult GetOfferStatus(
        Guid offerId,
        CredentialOfferService offerService)
    {
        var offer = offerService.GetOffer(offerId);
        if (offer == null)
            return Results.NotFound(new { error = $"Offer '{offerId}' not found" });

        return Results.Ok(new
        {
            offerId = offer.Id,
            credentialType = offer.CredentialType,
            status = offer.Status.ToString(),
            createdAt = offer.CreatedAt,
            expiresAt = offer.ExpiresAt
        });
    }
}

/// <summary>
/// Request to create a credential offer.
/// </summary>
public class CreateOfferRequest
{
    /// <summary>Wallet address of the issuing org (required).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string IssuerWalletAddress { get; init; }

    /// <summary>Tenant/org scope the offer is created under (required).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string TenantId { get; init; }

    /// <summary>Credential type (vct) to offer (required).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string CredentialType { get; init; }

    public Dictionary<string, object>? Claims { get; init; }
    public List<string>? DisclosablePaths { get; init; }
}
