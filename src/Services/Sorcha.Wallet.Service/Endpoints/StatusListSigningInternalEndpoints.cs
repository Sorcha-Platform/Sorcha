// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// TODO(095) / #1759 — signs IETF Token Status Lists with the issuing organisation's VC-issuance key,
/// so the Blueprint Service can publish lists a verifier resolves through <c>did:sorcha:org:{wallet}</c>.
/// The private key never leaves this service.
/// </summary>
public static class StatusListSigningInternalEndpoints
{
    /// <summary>The only principal allowed to have status lists signed — the service that publishes them.</summary>
    internal const string AllowedClientId = "service-blueprint";

    /// <summary>Maps the internal status-list signing endpoint.</summary>
    public static IEndpointRouteBuilder MapStatusListSigningInternalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/status-lists/ietf/sign", Sign)
            .WithTags("Internal")
            .ExcludeFromDescription()
            .RequireAuthorization(AuthorizationPolicies.RequireService)
            .WithName("SignIetfStatusList")
            .WithSummary("Sign an IETF Token Status List with the issuing organisation's VC-issuance key")
            .WithDescription(
                "Builds and signs a statuslist+jwt whose iss and kid are the organisation's credential-issuing " +
                "identity. Restricted to the Blueprint Service principal. 409 when the organisation has no active " +
                "VC-issuance key — there is no fallback key.");

        return app;
    }

    /// <summary>Handler — internal so the endpoint tests can invoke it directly.</summary>
    internal static async Task<IResult> Sign(
        [FromBody] SignStatusListTokenRequest request,
        HttpContext context,
        IStatusListTokenSigner signer,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(StatusListSigningInternalEndpoints));

        // This key signs the organisation's credentials too, so only the publisher of status lists may
        // use it — any other service token is refused before anything is signed (the #1397 pattern).
        var clientId = context.User.FindFirst("client_id")?.Value;
        if (!string.Equals(clientId, AllowedClientId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "SEC-AUDIT: principal {ClientId} attempted to sign a status list for organisation {OrgId}",
                clientId ?? "(none)", request.OrganizationId);
            return Results.Forbid();
        }

        byte[] entries;
        try
        {
            entries = Convert.FromBase64String(request.EntriesBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return Results.Problem("entriesBase64 is not valid Base64.", statusCode: StatusCodes.Status400BadRequest);
        }

        StatusListToken? token;
        try
        {
            token = await signer.SignAsync(
                new StatusListTokenSignRequest(
                    request.OrganizationId, request.Subject, request.Bits, entries, request.TtlSeconds),
                ct);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (token is null)
        {
            return Results.Problem(
                "The organisation has no active VC-issuance key, so its status list cannot be signed verifiably.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new SignStatusListTokenResponse
        {
            Jwt = token.Jwt,
            IssuerDid = token.IssuerDid,
            Kid = token.Kid,
        });
    }
}
