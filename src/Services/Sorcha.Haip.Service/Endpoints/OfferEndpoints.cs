// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Haip.Service.Services;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// Internal endpoints for credential offer management. Called by the Blueprint Service
/// when a credential issuance action targets an external HAIP wallet.
/// </summary>
public static class OfferEndpoints
{
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
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapGet("/{offerId:guid}", GetOfferStatus)
            .WithName("GetOfferStatus")
            .WithSummary("Get Credential Offer status (service-to-service)")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
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

        return Results.Ok(new
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
    public required string IssuerWalletAddress { get; init; }
    public required string TenantId { get; init; }
    public required string CredentialType { get; init; }
    public Dictionary<string, object>? Claims { get; init; }
    public List<string>? DisclosablePaths { get; init; }
}
