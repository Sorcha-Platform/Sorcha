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
    /// <summary>
    /// Maps the HAIP token endpoint.
    /// </summary>
    public static void MapTokenEndpoints(this WebApplication app)
    {
        app.MapPost("/token", ExchangeToken)
            .WithName("ExchangeToken")
            .WithTags("HAIP Token")
            .WithSummary("Exchange a pre-authorized code for an access token")
            .WithDescription(
                "OAuth 2.0 token endpoint supporting the pre-authorized code grant type. " +
                "Returns an access token and c_nonce for use in the credential request.")
            .Produces<CredentialTokenResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private static async Task<IResult> ExchangeToken(
        HttpContext httpContext,
        PreAuthCodeStore codeStore,
        NonceStore nonceStore,
        AccessTokenStore tokenStore,
        CredentialOfferService offerService,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // RFC 6749 §5.1: a response carrying a token MUST NOT be cached. Set here, before any
        // branch, so every exit path carries it — the success path is the one that matters, but
        // an error response is equally not worth caching, and setting it once removes the chance
        // that a future early-return quietly ships a cacheable token.
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";

        // Read form-encoded body (OAuth 2.0 §4.1.3)
        var form = await httpContext.Request.ReadFormAsync(ct);
        var grantType = form["grant_type"].FirstOrDefault() ?? string.Empty;
        var preAuthorizedCode = form["pre-authorized_code"].FirstOrDefault() ?? string.Empty;

        // Validate grant type
        if (grantType != "urn:ietf:params:oauth:grant-type:pre-authorized_code")
        {
            return Results.BadRequest(new { error = "unsupported_grant_type" });
        }

        if (string.IsNullOrWhiteSpace(preAuthorizedCode))
        {
            return Results.BadRequest(new { error = "invalid_request", error_description = "pre-authorized_code is required" });
        }

        // Redeem the pre-authorized code (one-time-use)
        var offerId = await codeStore.RedeemAsync(preAuthorizedCode, ct);
        if (offerId == null)
        {
            return Results.BadRequest(new { error = "invalid_grant", error_description = "Pre-authorized code is invalid, expired, or already redeemed" });
        }

        // Mark the offer as exchanged
        await offerService.MarkExchangedAsync(offerId.Value, ct);

        // Generate access token
        var tokenLifetime = configuration.GetValue<int>("Haip:TokenLifetimeSeconds", 300);
        var accessToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Generate c_nonce for the credential request proof
        var (cNonce, nonceExpiresIn) = await nonceStore.CreateAsync(ct);

        // Store the access token → offer mapping for later correlation
        await tokenStore.StoreAsync(accessToken, offerId.Value, ct);

        return Results.Ok(new CredentialTokenResponse
        {
            AccessToken = accessToken,
            ExpiresIn = tokenLifetime,
            CNonce = cNonce,
            CNonceExpiresIn = nonceExpiresIn
        });
    }
}
