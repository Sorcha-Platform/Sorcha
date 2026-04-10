// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// OAuth 2.0 token endpoint for the pre-authorized code grant (HAIP 1.0 MTI).
/// </summary>
public static class TokenEndpoints
{
    public static void MapTokenEndpoints(this WebApplication app)
    {
        app.MapPost("/token", ExchangeToken)
            .WithName("ExchangeToken")
            .WithTags("HAIP Token")
            .WithSummary("Exchange a pre-authorized code for an access token")
            .WithDescription(
                "OAuth 2.0 token endpoint supporting the pre-authorized code grant type. " +
                "Returns an access token and c_nonce for use in the credential request.")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private static async Task<IResult> ExchangeToken(
        [FromBody] TokenRequest request,
        PreAuthCodeStore codeStore,
        NonceStore nonceStore,
        CredentialOfferService offerService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // Validate grant type
        if (request.GrantType != "urn:ietf:params:oauth:grant-type:pre-authorized_code")
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }

        if (string.IsNullOrWhiteSpace(request.PreAuthorizedCode))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "pre-authorized_code is required" });
        }

        // Redeem the pre-authorized code (one-time-use)
        var offerId = await codeStore.RedeemAsync(request.PreAuthorizedCode, ct);
        if (offerId == null)
        {
            return Results.BadRequest(new { error = "invalid_grant", error_description = "Pre-authorized code is invalid, expired, or already redeemed" });
        }

        // Mark the offer as redeemed
        await offerService.MarkRedeemedAsync(offerId.Value, ct);

        // Generate access token
        var tokenLifetime = configuration.GetValue<int>("Haip:TokenLifetimeSeconds", 300);
        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Generate c_nonce for the credential request proof
        var (cNonce, nonceExpiresIn) = await nonceStore.CreateAsync(ct);

        return Results.Ok(new TokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = tokenLifetime,
            CNonce = cNonce,
            CNonceExpiresIn = nonceExpiresIn
        });
    }
}
